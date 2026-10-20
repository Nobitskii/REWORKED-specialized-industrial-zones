using System.Collections.Generic;
using Colossal;
using Colossal.Logging;
using Game.SceneFlow;
using Game.UI.InGame;
using Unity.Entities;

namespace SpecializedIndustryZones;

/// <summary>
/// One locale source holding the titles/descriptions of ALL specialized zones.
/// The old code registered one source per zone per language; this registers one per language.
/// </summary>
public class SpecializedIndustrialZoneLocaleSource(
    PrefabUISystem _prefabUISystem,
    ILog _log) : IDictionarySource
{
    private readonly List<(SpecializedZoneSpec Spec, Entity BaseZone, Entity Zone)> _zones = [];

    public int Count => _zones.Count;

    public void Add(SpecializedZoneSpec spec, Entity baseZone, Entity zone)
        => _zones.Add((spec, baseZone, zone));

    public IEnumerable<KeyValuePair<string, string>> ReadEntries(
        IList<IDictionaryEntryError> errors,
        Dictionary<string, int> indexCounts)
    {
        var activeDict = GameManager.instance.localizationManager.activeDictionary;

        foreach (var (spec, baseZone, zone) in _zones)
        {
            _prefabUISystem.GetTitleAndDescription(baseZone, out var baseTitleID, out var baseDescID);
            _prefabUISystem.GetTitleAndDescription(zone, out var titleID, out var descID);

            if (activeDict.TryGetValue(baseTitleID, out var baseTitle))
                yield return new KeyValuePair<string, string>(titleID, baseTitle + $" [{spec.Name}]");
            else
                _log.Warn($"Failed to get base title for {spec.Name}");

            if (activeDict.TryGetValue(baseDescID, out var baseDesc))
                yield return new KeyValuePair<string, string>(descID, baseDesc + $" [{spec.Description}]");
            else
                _log.Warn($"Failed to get base description for {spec.Name}");
        }
    }

    public void Unload()
    { }
}
