using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public static class CharacterRoleRegistry
    {
        public static readonly IReadOnlyDictionary<string, CharacterRole> Roles =
            new Dictionary<string, CharacterRole>(StringComparer.OrdinalIgnoreCase)
            {
                ["Cloud"] = CharacterRole.DPS,
                ["Barret"] = CharacterRole.Support,
                ["Tifa"] = CharacterRole.DPS,
                ["Aerith"] = CharacterRole.Healer,
                ["Red XIII"] = CharacterRole.Healer,
                ["Yuffie"] = CharacterRole.DPS,
                ["Cait Sith"] = CharacterRole.Support,
                ["Vincent"] = CharacterRole.DPS,
                ["Cid"] = CharacterRole.Support,
                ["Zack"] = CharacterRole.DPS,
                ["Sephiroth"] = CharacterRole.DPS,
                ["Glenn"] = CharacterRole.Tank,
                ["Matt"] = CharacterRole.Healer,
                ["Lucia"] = CharacterRole.Support,
                ["Angeal"] = CharacterRole.Tank,
                ["Sephiroth (Original)"] = CharacterRole.DPS,
            };

        public static CharacterRole GetRoleOrDefault(string characterName)
        {
            if (Roles.TryGetValue(characterName.Trim(), out var role))
            {
                return role;
            }

            return CharacterRole.DPS;
        }
    }
}
