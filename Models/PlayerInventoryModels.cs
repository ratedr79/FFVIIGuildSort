using System;

namespace FFVIIEverCrisisAnalyzer.Models
{
    public sealed class PlayerInventoryCatalogItem
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required int CharacterId { get; init; }
        public required string Character { get; init; }
        public required string CharacterPortraitUrl { get; init; }
        public required string ImageUrl { get; init; }
        public required string PreviewImageUrl { get; init; }
        public required string EquipmentType { get; init; }
        public required string Element { get; init; }
        public required string AbilityType { get; init; }
        public required string Range { get; init; }
        public required string AbilityText { get; init; }
        public bool HasCustomizations { get; init; }
        public bool SupportsViewLevels { get; init; }
    }
}
