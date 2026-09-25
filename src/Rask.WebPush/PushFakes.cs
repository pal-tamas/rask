namespace Rask.WebPush;

/// <summary><c>using var push = Push.Fake();</c> — this test's pushes go nowhere and can be asked about.</summary>
public static class PushFakes
{
    extension(Push)
    {
        /// <summary>Stands in for the battery for this test's flow alone; parallel tests never see each other's pushes.</summary>
        public static PushFake Fake() => new();
    }
}
