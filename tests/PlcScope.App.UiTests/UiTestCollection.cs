[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PlcScope.App.UiTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiTestCollection
{
    public const string Name = "UI automation";
}
