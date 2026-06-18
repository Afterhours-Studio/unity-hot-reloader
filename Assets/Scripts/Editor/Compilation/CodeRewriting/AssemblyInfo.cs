using System.Runtime.CompilerServices;

// The rewriters, walkers, and partial-class combiner are internal to UnityReloader.Core.
// UnityReloader.Editor drives the rewrite pipeline (DynamicCompilationBase, NewFields editor patch)
// and needs to reference these types without promoting them to the public API surface.
[assembly: InternalsVisibleTo("UnityReloader.Editor")]
