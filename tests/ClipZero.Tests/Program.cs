using ClipZero.Core;

namespace ClipZero.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--native-child")
        {
            return NativeTests.RunChild(args[1], args[2]);
        }
        if (args.Length == 3 && args[0] == "--ui-render")
        {
            return NativeTests.RunUIChild(args[1], args[2]);
        }

        int failures = 0;
        var tests = new (string Name, Action Run)[]
        {
            ("Single file copy is read and cleared", () =>
            {
                var fake = new FakeGateway();
                var service = new ClipboardService(fake);
                ClipboardSnapshot snapshot = service.ReadSnapshot();
                Equal(ClipboardState.Copied, snapshot.State);
                Equal(1, snapshot.FileCount);
                Equal("example.txt", snapshot.FileNames[0]);
                Equal(ClearOutcome.Cleared, service.Clear(snapshot.Sequence).Outcome);
                Equal(1, fake.EmptyCalls);
                Equal(ClipboardState.None, service.ReadSnapshot().State);
                Equal(3, fake.Disposals);
            }),
            ("Multiple files retain count and bounded preview", () =>
            {
                var fake = new FakeGateway { Files = new ClipboardFiles(100, Enumerable.Range(0, 100).Select(i => $"{i}.txt")) };
                ClipboardSnapshot snapshot = new ClipboardService(fake).ReadSnapshot();
                Equal(100, snapshot.FileCount);
                Equal(ClipboardFiles.MaximumPreviewCount, snapshot.FileNames.Count);
            }),
            ("Cut uses MOVE drop effect and clearing is permitted", () =>
            {
                var fake = new FakeGateway { Effect = 2 };
                var service = new ClipboardService(fake);
                Equal(ClipboardState.Cut, service.ReadSnapshot().State);
                Equal(ClearOutcome.Cleared, service.Clear(fake.Sequence).Outcome);
            }),
            ("Missing or ambiguous effect remains a file state", () =>
            {
                foreach (uint? effect in new uint?[] { null, 0, 3, 4 })
                {
                    var fake = new FakeGateway { Effect = effect };
                    var service = new ClipboardService(fake);
                    Equal(ClipboardState.Files, service.ReadSnapshot().State);
                    Equal(ClearOutcome.Cleared, service.Clear(fake.Sequence).Outcome);
                }
            }),
            ("Ordinary text is neither read nor cleared", () =>
            {
                var fake = new FakeGateway { HasFiles = false, ThrowOnRead = true };
                var service = new ClipboardService(fake);
                Equal(ClipboardState.None, service.ReadSnapshot().State);
                Equal(ClearOutcome.NoFiles, service.Clear(fake.Sequence).Outcome);
                Equal(0, fake.EmptyCalls);
                Equal(0, fake.ReadCalls);
            }),
            ("Copy changed before opening is never cleared", () =>
            {
                var fake = new FakeGateway();
                var service = new ClipboardService(fake);
                uint observed = service.ReadSnapshot().Sequence;
                fake.OnOpen = () => fake.Sequence++;
                Equal(ClearOutcome.Changed, service.Clear(observed).Outcome);
                Equal(0, fake.EmptyCalls);
            }),
            ("File-to-text race is never cleared", () =>
            {
                var fake = new FakeGateway();
                var service = new ClipboardService(fake);
                uint observed = service.ReadSnapshot().Sequence;
                fake.OnOpen = () => { fake.Sequence++; fake.HasFiles = false; };
                ClearResult result = service.Clear(observed);
                Equal(ClearOutcome.Changed, result.Outcome);
                Equal(ClipboardState.None, result.Snapshot.State);
                Equal(0, fake.EmptyCalls);
            }),
            ("Delayed-rendering sequence change cannot be cleared", () =>
            {
                var fake = new FakeGateway();
                var service = new ClipboardService(fake);
                uint observed = service.ReadSnapshot().Sequence;
                fake.OnRead = () => fake.Sequence++;
                Equal(ClearOutcome.Changed, service.Clear(observed).Outcome);
                Equal(0, fake.EmptyCalls);
            }),
            ("Delayed-rendering snapshot is marked unavailable", () =>
            {
                var fake = new FakeGateway();
                fake.OnRead = () => fake.Sequence++;
                Equal(ClipboardState.Unavailable, new ClipboardService(fake).ReadSnapshot().State);
            }),
            ("Busy clipboard recovers without clearing newer content", () =>
            {
                var fake = new FakeGateway();
                var service = new ClipboardService(fake);
                uint observed = service.ReadSnapshot().Sequence;
                fake.Busy = true;
                Equal(ClipboardState.Unavailable, service.ReadSnapshot().State);
                Equal(ClearOutcome.Busy, service.Clear(observed).Outcome);
                fake.Busy = false;
                fake.Sequence++;
                Equal(ClearOutcome.Changed, service.Clear(observed).Outcome);
                Equal(0, fake.EmptyCalls);
            }),
            ("Unreadable file data cannot be cleared and lock is released", () =>
            {
                var fake = new FakeGateway { ThrowOnRead = true };
                var service = new ClipboardService(fake);
                Equal(ClipboardState.Unavailable, service.ReadSnapshot().State);
                Equal(ClearOutcome.Failed, service.Clear(fake.Sequence).Outcome);
                Equal(0, fake.EmptyCalls);
                Equal(2, fake.Disposals);
                True(!fake.IsOpen);
            }),
            ("EmptyClipboard failure preserves the original file state", () =>
            {
                var fake = new FakeGateway { EmptySucceeds = false };
                var service = new ClipboardService(fake);
                Equal(ClearOutcome.Failed, service.Clear(fake.Sequence).Outcome);
                True(fake.HasFiles);
                True(!fake.IsOpen);
            }),
            ("EmptyClipboard exception is contained and lock is released", () =>
            {
                var fake = new FakeGateway { ThrowOnEmpty = true };
                Equal(ClearOutcome.Failed, new ClipboardService(fake).Clear(fake.Sequence).Outcome);
                True(!fake.IsOpen);
            }),
            ("No unlocked format reads or clipboard mutations", () =>
            {
                var fake = new FakeGateway();
                var service = new ClipboardService(fake);
                uint sequence = service.ReadSnapshot().Sequence;
                Equal(ClearOutcome.Cleared, service.Clear(sequence).Outcome);
                True(!fake.IsOpen);
            }),
            ("Preview model does not retain a mutable names collection", () =>
            {
                string[] names = [new string('x', 1000)];
                var files = new ClipboardFiles(1, names);
                names[0] = "modified";
                Equal(ClipboardFiles.MaximumDisplayNameLength + 1, files.FileNames[0].Length);
            })
        };

        foreach ((string name, Action test) in tests)
        {
            try { test(); Console.WriteLine($"PASS {name}"); }
            catch (Exception exception) { failures++; Console.WriteLine($"FAIL {name}: {exception.Message}"); }
        }

        Console.WriteLine($"Core: {tests.Length - failures}/{tests.Length} passed.");
        failures += ExplorerUndoTests.Run();
        if (args.Contains("--ui", StringComparer.Ordinal))
        {
            failures += ReadOnlyNativeTests.Run();
        }
        if (args.Contains("--native", StringComparer.Ordinal))
        {
            failures += NativeTests.RunIsolated();
        }
        int portableIndex = Array.IndexOf(args, "--portable");
        if (portableIndex >= 0)
        {
            string? executable = portableIndex + 1 < args.Length && !args[portableIndex + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[portableIndex + 1] : null;
            failures += PortableSmokeTests.Run(portableExecutable: executable);
        }
        if (args.Contains("--development", StringComparer.Ordinal))
        {
            failures += PortableSmokeTests.Run(development: true);
        }

        return failures == 0 ? 0 : 1;
    }

    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
        }
    }

    internal static void True(bool value)
    {
        if (!value) { throw new InvalidOperationException("Expected true."); }
    }

    private sealed class FakeGateway : IClipboardGateway
    {
        public uint Sequence { get; set; } = 42;
        public bool HasFiles { get; set; } = true;
        public ClipboardFiles Files { get; init; } = new(1, ["example.txt"]);
        public uint? Effect { get; init; } = 1;
        public bool Busy { get; set; }
        public bool ThrowOnRead { get; init; }
        public bool EmptySucceeds { get; init; } = true;
        public bool ThrowOnEmpty { get; init; }
        public bool IsOpen { get; private set; }
        public int EmptyCalls { get; private set; }
        public int ReadCalls { get; private set; }
        public int Disposals { get; private set; }
        public Action? OnOpen { get; set; }
        public Action? OnRead { get; set; }

        public bool TryOpen(out IClipboardSession? session)
        {
            if (Busy) { session = null; return false; }
            True(!IsOpen);
            OnOpen?.Invoke();
            IsOpen = true;
            session = new Session(this);
            return true;
        }

        private sealed class Session(FakeGateway owner) : IClipboardSession
        {
            public uint Sequence { get { AssertOpen(); return owner.Sequence; } }
            public bool ContainsFileDrop { get { AssertOpen(); return owner.HasFiles; } }
            public ClipboardFiles ReadFiles()
            {
                AssertOpen(); owner.ReadCalls++;
                if (owner.ThrowOnRead) { throw new InvalidDataException("Synthetic read failure."); }
                owner.OnRead?.Invoke();
                return owner.Files;
            }
            public uint? ReadDropEffect() { AssertOpen(); return owner.Effect; }
            public bool Empty()
            {
                AssertOpen(); owner.EmptyCalls++;
                if (owner.ThrowOnEmpty) { throw new InvalidOperationException("Synthetic empty failure."); }
                if (!owner.EmptySucceeds) { return false; }
                owner.HasFiles = false; owner.Sequence++;
                return true;
            }
            public void Dispose() { AssertOpen(); owner.IsOpen = false; owner.Disposals++; }
            private void AssertOpen() => True(owner.IsOpen);
        }
    }
}
