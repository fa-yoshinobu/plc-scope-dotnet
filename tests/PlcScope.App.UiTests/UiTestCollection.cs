[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace PlcScope.App.UiTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiTestCollection
{
    public const string Name = "UI automation";
}
