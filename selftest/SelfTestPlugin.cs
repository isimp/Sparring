using BepInEx;

namespace Sparring
{
    /// <summary>
    /// The self-test as a plugin of its own beside Sparring: it registers with Sparring as it
    /// loads (<see cref="SelfTestHost"/>). Installing it is what turns the self-test on, so a
    /// release, which packages Sparring.dll alone, has nothing of it.
    /// </summary>
    [BepInPlugin(Guid, "Sparring self-test", "1.0.0")]
    [BepInDependency(Plugin.Guid)]
    [BepInProcess("valheim.exe")]
    public class SelfTestPlugin : BaseUnityPlugin
    {
        public const string Guid = "isimp.Sparring.SelfTest";

        private void Awake() => SelfTestHost.Register(new SelfTest(this));
    }
}
