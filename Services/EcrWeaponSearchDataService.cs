using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace FFVIIEverCrisisAnalyzer.Services
{
    /// <summary>The Gear Search data with the ECR (Reawakened) Rebalancing changes laid over it.</summary>
    public sealed class EcrWeaponSearchDataService : WeaponSearchDataService
    {
        public EcrWeaponSearchDataService(
            ILogger<WeaponSearchDataService> logger,
            IWebHostEnvironment environment,
            EcrOverlayStore overlay,
            GearImageCatalog? gearImageCatalog = null)
            : base(logger, environment, gearImageCatalog, overlay)
        {
            Overlay = overlay;
        }

        public EcrOverlayStore Overlay { get; }

        /// <summary>Rebuilds the overlay if the launcher's recipe changed, and reloads when it did.</summary>
        public void RefreshFromRecipe()
        {
            if (Overlay.RefreshFromRecipe())
                ReloadData();
        }
    }
}
