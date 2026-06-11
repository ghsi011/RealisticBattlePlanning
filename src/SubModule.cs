using Bannerlord.UIExtenderEx;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RealisticBattlePlanning
{
    public class SubModule : MBSubModuleBase
    {
        public const string ModId = "RealisticBattlePlanning";
        public const string ModName = "Realistic Battle Planning";

        private static readonly UIExtender UIExtender = UIExtender.Create(ModId);
        private static readonly Harmony Harmony = new(ModId);

        private bool _loadedToastShown;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            Debug.Print($"[{ModId}] OnSubModuleLoad");

            Harmony.PatchAll(typeof(SubModule).Assembly);

            UIExtender.Register(typeof(SubModule).Assembly);
            UIExtender.Enable();
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            if (_loadedToastShown) return;
            _loadedToastShown = true;
            InformationManager.DisplayMessage(
                new InformationMessage($"{ModName} loaded.", Colors.Green));
        }
    }
}
