using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

internal static partial class QuantityItemReferenceAnalyzer
{
    private sealed partial class LootBuffSources
    {
        private void ReadCurioEffects(IReadOnlyList<ScannedContentFile> roots, Action<NativeEffectReferences.Reference> add)
        {
            foreach (var file in roots.Where(file => file.IsCurioTypeFile))
            {
                if (file.ReadFailed) { Incomplete(file.MountedPath); continue; }
                var rows = NativeCurioCsvReader.ReadFromText(file.Text, file.File.Path, 24, mapping: false);
                foreach (var block in NativeCurioCsvReader.TypeBlocks(rows))
                {
                    if (block.Count == 0 || block[0].Count < 3 || block[0].Fields[2].Length == 0)
                        throw new InvalidDataException("Invalid curio type block.");
                    var itemSection = false;
                    foreach (var row in block)
                    {
                        var fields = row.Fields;
                        if (!itemSection && fields[4] == "ITEM") { itemSection = true; continue; }
                        if (itemSection)
                        {
                            if (fields[4].Length == 0 || fields[5] != "Effect") continue;
                            var item = Bound(fields[4], 255);
                            var split = item.IndexOf('#');
                            var type = split < 0 ? "supply" : item[(split + 1)..];
                            var id = split < 0 ? item : item[..split];
                            if (!itemsComplete) Incomplete("Curio item interaction has an incomplete inventory library.");
                            if (!useItems.ContainsKey((NativeResourceIdentity.HashCString(type), NativeResourceIdentity.HashCString(id)))) continue;
                        }
                        else
                        {
                            if (fields[4] != "Effect") continue;
                            var weight = NativeDarkestReader.ReadIntPrefix(fields[5])
                                ?? throw new InvalidDataException("Invalid curio result weight.");
                            if (weight == 0) continue;
                        }
                        // Unlike Loot, Effect results are three independent weighted
                        // alternatives, each stored in its own 64-byte buffer.
                        foreach (var column in new[] { 7, 10, 13 })
                        {
                            if (column + 1 >= row.Count || fields[column].Length == 0) continue;
                            var weight = fields[column + 1].Length == 0 ? 1 : NativeDarkestReader.ReadIntPrefix(fields[column + 1]);
                            if (weight is null) { Incomplete("Invalid curio Effect weight."); continue; }
                            if (weight <= 0) continue;
                            add(new(Bound(fields[column], 63), Combat: false));
                        }
                    }
                }
            }
        }

        private void ReadActivities(IReadOnlyDictionary<uint, JsonElement> buffs, Action<NativeEffectReferences.Reference> add)
        {
            // The derived building factories (0x534AB0/0x555DF0/0x54F830)
            // create these activities only; arbitrary JSON IDs create nothing.
            foreach (var (building, activities) in new[]
            {
                ("abbey", new[] { "meditation", "prayer", "flagellation" }),
                ("tavern", new[] { "bar", "gambling", "brothel" }),
                ("sanitarium", new[] { "treatment", "disease_treatment" })
            })
            foreach (var document in Documents("campaign/town/buildings", (path, dlc, manifest) =>
                         NativeResourceFileRules.IsBuildingFile(path, building, dlc, manifest)))
            {
                var data = Object(document, "data");
                foreach (var activity in Objects(data, "activities"))
                {
                    var id = NativeResourceIdentity.HashCString(Text(activity, "id", required: true));
                    if (!activities.Any(name => NativeResourceIdentity.HashCString(name) == id)) continue;
                    var sideEffects = Object(Object(activity, "data"), "side_effects");
                    if (Number(sideEffects, "chance") <= 0) continue;
                    foreach (var result in Objects(sideEffects, "results"))
                    {
                        if (Text(result, "type", 63) != "apply_buff" || Number(result, "chance") <= 0) continue;
                        foreach (var choice in Objects(result, "data"))
                        {
                            if (Number(choice, "chance") <= 0) continue;
                            // Load verifies the full hash, but stores a 63-byte
                            // token for the later ApplyBuff lookup (0x53C397).
                            foreach (var name in Strings(choice, "buff_library_ids"))
                                if (buffs.ContainsKey(NativeResourceIdentity.HashCString(name))) add(new(Bound(name, 63), Buff: true));
                        }
                    }
                }
            }
        }

        private void ReadTraps(Action<NativeEffectReferences.Reference> add)
        {
            var sourceIssues = new List<string>();
            foreach (var id in BattleRoomAttachmentCatalog.ReadTrapEffectReferences(content.Sources, sourceIssues))
                add(new(id, Combat: false));
            foreach (var issue in sourceIssues) Incomplete(issue);
        }
    }
}
