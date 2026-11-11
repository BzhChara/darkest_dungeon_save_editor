internal static partial class ContractSuite
{
    private static JsonObject DuplicateQuirk(string id, double chance, bool buff = false, bool positive = true) => new()
    {
        ["id"] = id, ["random_chance"] = chance, ["is_positive"] = positive, ["is_disease"] = false,
        ["buffs"] = buff ? new JsonArray("trigger") : new JsonArray(), ["incompatible_quirks"] = new JsonArray()
    };
    private static string QuirkLibrary(params JsonObject[] entries) =>
        new JsonObject { ["quirks"] = new JsonArray(entries.Cast<JsonNode>().ToArray()) }.ToJsonString();

    private static void VerifyDuplicateQuirkSources(string root)
    {
        foreach (var kind in new[] { "local", "workshop", "dlc-mod" })
        {
            foreach (var test in new[] { "first-random-buff", "first-special-buff", "last-special-buff", "alias-random-buff",
                "unique-special-actout", "first-special-actout", "first-random-actout", "alias-special-actout", "alias-random-actout",
                "underflow-actout", "first-json-member", "first-root-member" })
            {
                var f = new LootSourceFixture(Path.Combine(root, kind, test), kind);
                var alias = test.Contains("alias", StringComparison.Ordinal);
                var firstId = alias ? "aZ" : "probe";
                var lastId = alias ? "b%" : "probe";
                var actout = test.Contains("actout", StringComparison.Ordinal) || test.Contains("member", StringComparison.Ordinal);
                var first = DuplicateQuirk(firstId, test.Contains("random", StringComparison.Ordinal) ? 1 : 0, buff: !actout);
                var last = DuplicateQuirk(lastId, 0, buff: test == "last-special-buff");
                if (test == "underflow-actout") first["random_chance"] = 1e-50;
                var library = test == "unique-special-actout" ? QuirkLibrary(last) : QuirkLibrary(first, last);
                if (test == "first-json-member") library = library.Replace("\"random_chance\":0", "\"random_chance\":0,\"random_chance\":1");
                if (test == "first-root-member") library = "{\"quirks\":[]," + library[1..];
                f.Write("shared/quirk/query.quirk_library.json", library);
                if (actout)
                    f.Write("shared/quirk/query.quirk_act_outs.json", "{\"quirk_act_outs\":[{\"quirk_id\":\"" + lastId + "\"," + RuntimeActOut() + "}]}");
                // Establish named acquisition as well as the random path. It
                // always selects the last definition, never the act-out owner.
                File.AppendAllText(f.Effects, "effect: .name acquire .disease \"" + lastId + "\"\n");
                f.Actor("combat_skill: .id s0 .level 0 .effect acquire\n");
                var expected = test is "first-random-buff" or "last-special-buff" or "alias-random-buff" or
                    "unique-special-actout" or "first-random-actout" or "alias-random-actout"
                    ? QuantityItemReferenceStatus.ConfirmedActive : QuantityItemReferenceStatus.SuspectedUnused;
                f.Check(expected, "Quirk definition identity: " + test);
            }
            var overlay = new LootSourceFixture(Path.Combine(root, kind, "overlay"), kind);
            const string path = "shared/quirk/query.quirk_library.json";
            var low = WriteMultiMash(overlay.Files.Content.Sources.Single(s => s.Id == "base").Directory, path,
                QuirkLibrary(DuplicateQuirk("probe", 1, buff: true)));
            var high = overlay.Write(path, QuirkLibrary(DuplicateQuirk("probe", 0)));
            overlay.Check(QuantityItemReferenceStatus.SuspectedUnused, "Same-path replacement removes the old random object too");
            File.WriteAllText(low, "{");
            File.WriteAllText(high, QuirkLibrary(DuplicateQuirk("probe", 1, buff: true), DuplicateQuirk("probe", 0)));
            overlay.Check(QuantityItemReferenceStatus.ConfirmedActive, "Shadowed failures cannot remove a valid random-object root");
            // A later distinct file supplies the last ID lookup, but must not
            // remove an earlier positive-chance instance from the random pool.
            overlay.Write("shared/quirk/z.quirk_library.json", QuirkLibrary(DuplicateQuirk("probe", 0)));
            overlay.Check(QuantityItemReferenceStatus.ConfirmedActive, "Cross-file duplicates preserve random objects");
            var unknown = overlay.Write("shared/quirk/m.quirk_library.json", "{");
            overlay.Check(QuantityItemReferenceStatus.AnalysisIncomplete, "Unreadable quirk library leaves pool/binding identity unknown", globalIncomplete: true);
            overlay.Actor("combat_skill: .id s0 .level 0 .effect gain\n");
            overlay.Check(QuantityItemReferenceStatus.ConfirmedActive, "An independent actor root survives quirk uncertainty", globalIncomplete: true);
            overlay.Actor("");
            File.WriteAllText(unknown, "{\"quirks\":[]}");
            overlay.Check(QuantityItemReferenceStatus.ConfirmedActive, "Repair restores duplicate-object certainty");
            File.Delete(unknown);
            overlay.Check(QuantityItemReferenceStatus.AnalysisIncomplete, "A missing manifest-listed quirk file leaves the pool incomplete", globalIncomplete: true);
            Console.WriteLine($"PASS: {kind} duplicate quirk Buff/act-out identities, hash aliases, float/member rules and provider certainty.");
        }
    }
}
