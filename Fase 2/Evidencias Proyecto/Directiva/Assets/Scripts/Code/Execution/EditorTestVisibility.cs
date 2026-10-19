using System.Runtime.CompilerServices;

// Runtime mutation primitives stay internal. Unity compiles Assets/Editor scripts in
// Assembly-CSharp-Editor, which needs friend access for invariant-focused tests.
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
