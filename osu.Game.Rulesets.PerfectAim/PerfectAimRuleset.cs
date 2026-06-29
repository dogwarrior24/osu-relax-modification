// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.PerfectAim.Mods;

namespace osu.Game.Rulesets.PerfectAim
{
    /// <summary>
    /// A thin custom ruleset that reuses all of osu! standard's gameplay but exposes one extra
    /// mod (<see cref="OsuModPerfectAim"/>). It is designed to be built into its own
    /// <c>osu.Game.Rulesets.*.dll</c> and dropped into the game's user <c>rulesets/</c> folder,
    /// so it survives client updates without replacing the official osu! ruleset assembly.
    /// </summary>
    public class PerfectAimRuleset : OsuRuleset, ILegacyRuleset
    {
        public override string Description => "osu! (perfect aim)";

        public override string ShortName => "perfectaim";

        // Re-implement ILegacyRuleset to report a non-legacy online ID (-1).
        //
        // OsuRuleset implements ILegacyRuleset with LegacyID => 0, and Ruleset's constructor uses
        // (this as ILegacyRuleset)?.LegacyID to populate RulesetInfo.OnlineID. Leaving that at 0
        // would collide with the built-in osu! ruleset. A non-zero online ID is also what makes
        // osu! (mode 0) beatmaps show up as convertible maps when this ruleset is selected — see
        // BeatmapInfoExtensions.AllowGameplayWithRuleset, which only allows conversion when the
        // target ruleset's OnlineID != 0.
        int ILegacyRuleset.LegacyID => -1;

        public override IEnumerable<Mod> GetModsFor(ModType type)
        {
            // start from the full osu! standard mod list, then add our extra practice mod.
            IEnumerable<Mod> mods = base.GetModsFor(type);

            if (type == ModType.Automation)
                mods = mods.Append(new OsuModPerfectAim());

            return mods;
        }
    }
}
