// FactAttribute.cs
//
// What this file does: it defines the [Fact] marker that says "this method is a test".
//
// Why the namespace is Xunit: this is deliberate, and it is the whole point of the
// TestKit folder. The build machine cannot reach the NuGet package feed, so the real
// xUnit library cannot be downloaded (see KARARLAR.md KARAR-006). Rather than invent
// our own test syntax, we implement the small part of xUnit's surface that we actually
// use, under xUnit's own name. The consequence: every test file in this project is
// written exactly as it would be against the real library.
//
// The day the feed becomes reachable, the migration is: add the three PackageReference
// lines to Atm.Tests.csproj, delete this TestKit folder. Not one test file changes.
//
// Careful: nothing outside tests/ may ever reference this namespace. It is a stand-in
// for a testing library, not part of the simulator.

namespace Xunit;

/// <summary>Marks a parameterless method as a test case to be discovered and run.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class FactAttribute : Attribute
{
    /// <summary>When set, the test is reported as skipped and the reason is printed.</summary>
    public string? Skip { get; set; }
}
