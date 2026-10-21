using UnityEditor;

namespace Directiva.EditorTests
{
    /// <summary>Stable Unity menu paths; individual test selection lives in the Editor window.</summary>
    internal static class DirectivaTestMenu
    {
        [MenuItem("Directiva/Tests/TestAll", false, 100)]
        private static void TestAll() => DirectivaTestRunner.StartAll();

        [MenuItem("Directiva/Tests/Open Test Runner and Reports...", false, 101)]
        private static void Open() => DirectivaTestWindow.ShowWindow();

        [MenuItem("Directiva/Tests/DIL/TestEverything", false, 110)]
        private static void TestDIL() => DirectivaTestRunner.StartCategory("DIL");

        [MenuItem("Directiva/Tests/DIL/Parser/TestEverything", false, 120)]
        private static void Parser() => DirectivaTestRunner.StartCategory("DIL", "Parser");
        [MenuItem("Directiva/Tests/DIL/VM/TestEverything", false, 120)]
        private static void VM() => DirectivaTestRunner.StartCategory("DIL", "VM");
        [MenuItem("Directiva/Tests/DIL/Controller/TestEverything", false, 120)]
        private static void Controller() => DirectivaTestRunner.StartCategory("DIL", "Controller");
        [MenuItem("Directiva/Tests/DIL/Numeric/TestEverything", false, 120)]
        private static void Numeric() => DirectivaTestRunner.StartCategory("DIL", "Numeric");
        [MenuItem("Directiva/Tests/DIL/References/TestEverything", false, 120)]
        private static void References() => DirectivaTestRunner.StartCategory("DIL", "References");
        [MenuItem("Directiva/Tests/DIL/Hashes/TestEverything", false, 120)]
        private static void Hashes() => DirectivaTestRunner.StartCategory("DIL", "Hashes");
        [MenuItem("Directiva/Tests/DIL/Lists/TestEverything", false, 120)]
        private static void Lists() => DirectivaTestRunner.StartCategory("DIL", "Lists");
        [MenuItem("Directiva/Tests/DIL/Sets/TestEverything", false, 120)]
        private static void Sets() => DirectivaTestRunner.StartCategory("DIL", "Sets");
        [MenuItem("Directiva/Tests/DIL/SetAlgebra/TestEverything", false, 120)]
        private static void SetAlgebra() => DirectivaTestRunner.StartCategory("DIL", "SetAlgebra");
        [MenuItem("Directiva/Tests/DIL/SetMemory/TestEverything", false, 120)]
        private static void SetMemory() => DirectivaTestRunner.StartCategory("DIL", "SetMemory");
        [MenuItem("Directiva/Tests/DIL/SetConcurrency/TestEverything", false, 120)]
        private static void SetConcurrency() => DirectivaTestRunner.StartCategory("DIL", "SetConcurrency");
        [MenuItem("Directiva/Tests/DIL/SetRobustness/TestEverything", false, 120)]
        private static void SetRobustness() => DirectivaTestRunner.StartCategory("DIL", "SetRobustness");
        [MenuItem("Directiva/Tests/DIL/Members/TestEverything", false, 120)]
        private static void Members() => DirectivaTestRunner.StartCategory("DIL", "Members");
        [MenuItem("Directiva/Tests/DIL/Dictionaries/TestEverything", false, 120)]
        private static void Dictionaries() => DirectivaTestRunner.StartCategory("DIL", "Dictionaries");
        [MenuItem("Directiva/Tests/DIL/DictViews/TestEverything", false, 120)]
        private static void DictViews() => DirectivaTestRunner.StartCategory("DIL", "DictViews");
        [MenuItem("Directiva/Tests/DIL/DictMemory/TestEverything", false, 120)]
        private static void DictMemory() => DirectivaTestRunner.StartCategory("DIL", "DictMemory");
        [MenuItem("Directiva/Tests/DIL/DictConcurrency/TestEverything", false, 120)]
        private static void DictConcurrency() => DirectivaTestRunner.StartCategory("DIL", "DictConcurrency");
        [MenuItem("Directiva/Tests/DIL/DictRobustness/TestEverything", false, 120)]
        private static void DictRobustness() => DirectivaTestRunner.StartCategory("DIL", "DictRobustness");
        [MenuItem("Directiva/Tests/DIL/ListMemory/TestEverything", false, 120)]
        private static void ListMemory() => DirectivaTestRunner.StartCategory("DIL", "ListMemory");
        [MenuItem("Directiva/Tests/DIL/ListConcurrency/TestEverything", false, 120)]
        private static void ListConcurrency() => DirectivaTestRunner.StartCategory("DIL", "ListConcurrency");
        [MenuItem("Directiva/Tests/DIL/Memory/TestEverything", false, 120)]
        private static void Memory() => DirectivaTestRunner.StartCategory("DIL", "Memory");
        [MenuItem("Directiva/Tests/DIL/Concurrency/TestEverything", false, 120)]
        private static void Concurrency() => DirectivaTestRunner.StartCategory("DIL", "Concurrency");
        [MenuItem("Directiva/Tests/DIL/Errors/TestEverything", false, 120)]
        private static void Errors() => DirectivaTestRunner.StartCategory("DIL", "Errors");
        [MenuItem("Directiva/Tests/DIL/Robustness/TestEverything", false, 120)]
        private static void Robustness() => DirectivaTestRunner.StartCategory("DIL", "Robustness");
        [MenuItem("Directiva/Tests/DIL/Regression/TestEverything", false, 120)]
        private static void Regression() => DirectivaTestRunner.StartCategory("DIL", "Regression");
    }
}
