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
        // Every zone clones every matching building, so 12 zones means ~3x the prefab work of 4 zones.
        // Set to true to get the extra 8 variant zones back (slower loading).
        public const bool IncludeVariantZones = false;

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
