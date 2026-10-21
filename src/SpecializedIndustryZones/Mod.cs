using Colossal.Logging;
using Colossal.UI;
using Game;
using Game.Modding;
using Game.Prefabs;
using Game.SceneFlow;
using System.IO;

namespace SpecializedIndustryZones
{
    public class Mod : IMod
    {
        public const string HostName = "speciz";
        public static ILog log = LogManager.GetLogger($"{nameof(SpecializedIndustryZones)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        // The original mod created 12 zones (4 industries x General / Manufacturing only / Warehouses only).
        // Each switch below controls one of the two variant types. Set one to false to leave those 4 zones out.
        public static readonly bool IncludeWarehousesOnlyZones = true;
        public static readonly bool IncludeManufacturingOnlyZones = false;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                log.Info($"Current mod asset at {asset.path}");

                var path = Path.GetDirectoryName(asset.GetMeta().path);
                UIManager.defaultUISystem.AddHostLocation(HostName, Path.Combine(path, "Assets/"));
            }
            else
            {
                log.Error("Could not find the mod's executable asset; zone icons will not load.");
            }

            updateSystem.UpdateBefore<SpecializedZoningSystem, PrefabSystem>(SystemUpdatePhase.MainLoop);
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
        }
    }
}
