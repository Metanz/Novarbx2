using System.Text.RegularExpressions;
using Roblox.Libraries;
using Roblox.Libraries.RobloxApi;
using Roblox.Services;
using Roblox.Services.App.FeatureFlags;
using Roblox.Services.Exceptions;
using ServiceProvider = Roblox.Services.ServiceProvider;
using Roblox.Models.Assets;
using Type = Roblox.Models.Assets.Type;

namespace Roblox.Website.WebsiteModels.Asset;

public class AssetTypeNotAllowedException : Exception
{
    public AssetTypeNotAllowedException(Roblox.Models.Assets.Type? type = null) 
        : base("Asset type is invalid or not in allowedTypes list: " + type)
    {
    }
}

public class MigrateItem
{
    public long assetId { get; set; }
    public long assetVersionId { get; set; }

    public MigrateItem(long assetId, long assetVersionId)
    {
        this.assetId = assetId;
        this.assetVersionId = assetVersionId;
    }

    private static Regex assetIdUrlRegex = new("\\?id=([0-9]+)");

    private static async Task<WebsiteModels.Asset.MigrateItem?> TryGetMigratedItem(long assetId)
    {
        using var assets = ServiceProvider.GetOrCreate<AssetsService>();
        try
        {
            // Check if already exists
            var ourAssetId = await assets.GetAssetIdFromRobloxAssetId(assetId);
            var latestVersion = await assets.GetLatestAssetVersion(ourAssetId);
            return new WebsiteModels.Asset.MigrateItem(ourAssetId, latestVersion.assetVersionId);
        }
        catch (RecordNotFoundException)
        {
            // Don't care, it just doesn't exist yet
        }

        return null;
    }
    
    /// <summary>
    /// Helper to safely buffer a network stream into a MemoryStream so we can seek (reset Position = 0)
    /// </summary>
    private static async Task<MemoryStream> BufferStreamAsync(Stream input)
    {
        var memStream = new MemoryStream();
        await input.CopyToAsync(memStream);
        memStream.Position = 0;
        return memStream;
    }
    
    public static async Task<MigrateItem> MigrateItemFromRoblox(string robloxUrl, bool isForSale = false,
        int? price = null, IEnumerable<Models.Assets.Type>? allowedTypes = null, ProductDataResponse? defaultResponse = null, bool doRender = true, bool autoApprove = false)
    {
        using var assets = ServiceProvider.GetOrCreate<AssetsService>();
        var robloxApi = new RobloxApi();
        FeatureFlags.FeatureCheck(FeatureFlag.UploadContentEnabled);
        
        var assetId = Libraries.Assets.UrlUtilities.GetAssetIdFromUrl(robloxUrl);
        
        // first try, likely (prevents unneeded locks if already migrated)
        var existing = await TryGetMigratedItem(assetId);
        if (existing != null)
            return existing;
        
        await using var migrationLock = await Services.Cache.redLock.CreateLockAsync("MigrateItemFromRobloxV1:" + assetId, TimeSpan.FromSeconds(30));
        if (!migrationLock.IsAcquired)
            throw new LockNotAcquiredException();
        
        // second try, very unlikely but prevents duplicates if two threads lock at the same time.
        existing = await TryGetMigratedItem(assetId);
        if (existing != null)
            return existing;

        var robloxDetails = defaultResponse ?? await robloxApi.GetProductInfoAssetDelivery(assetId, Roblox.Configuration.RobloxOpenCloudApiKey);
        
        if (allowedTypes != null)
        {
            if (robloxDetails.AssetTypeId == null || !allowedTypes.Contains(robloxDetails.AssetTypeId.Value))
            {
                throw new AssetTypeNotAllowedException(robloxDetails.AssetTypeId);
            }
        }

        Stream? content;
        long? contentId = null;
        
        if (robloxDetails.AssetTypeId == Models.Assets.Type.Audio)
        {
            await using var networkStream = await robloxApi.GetAssetAudioContent(assetId);
            content = await BufferStreamAsync(networkStream);
        }
        else
        {
            if (robloxDetails is ProductInfoWithAssetDelivery extended && !string.IsNullOrEmpty(extended.location))
            {
                await using var networkStream = await robloxApi.GetStreamAsync(extended.location);   
                content = await BufferStreamAsync(networkStream);
            }
            else
            {
                await using var networkStream = await robloxApi.GetAssetContent(assetId, Roblox.Configuration.RobloxOpenCloudApiKey);
                content = await BufferStreamAsync(networkStream);
            }
            
            if (robloxDetails.AssetTypeId is Models.Assets.Type.TeeShirt or Models.Assets.Type.Shirt or Models.Assets.Type.Pants)
            {
                using var reader = new StreamReader(content, leaveOpen: true);
                var str = await reader.ReadToEndAsync();
                content.Position = 0; // Safe now because it's a MemoryStream

                var robloxUrls = assetIdUrlRegex.Match(str);
                if (robloxUrls.Success)
                {
                    contentId = long.Parse(robloxUrls.Groups[1].Value);
                }
                else
                {
                    throw new Exception("Could not match for robloxUrl in clothing XML.");
                }
            }
        }

        var disableRender = !doRender;
#if DEBUG
        disableRender = true;
#endif
        var modState = autoApprove ? ModerationStatus.ReviewApproved : ((robloxDetails?.AssetTypeId is Type.Animation or Type.SolidModel or Type.Lua or Type.Mesh or Type.MeshPart or Type.Model)
            ? ModerationStatus.ReviewApproved
            : ModerationStatus.AwaitingApproval);

        if (contentId != null)
        {
            await using var networkImageData = await robloxApi.GetAssetContent((long)contentId, Roblox.Configuration.RobloxOpenCloudApiKey);
            var imageData = await BufferStreamAsync(networkImageData);

            if (robloxDetails.AssetTypeId == null)
                throw new Exception("Null " + nameof(robloxDetails.AssetTypeId));
                
            var ok = await assets.ValidateClothing(imageData, robloxDetails.AssetTypeId.Value);
            if (ok == null)
            {
                throw new Exception("ValidateClothing() returned false");
            }

            if (robloxDetails.Name == null)
                throw new Exception("Null " + nameof(robloxDetails.Name));

            // upload content
            imageData.Position = 0;
            var shirtResult = await assets.CreateAsset(robloxDetails.Name, null, 2500, CreatorType.User, 2500, imageData,
                Models.Assets.Type.Image, Genre.All, modState, DateTime.UtcNow, DateTime.UtcNow,
                contentId, disableRender);

            imageData.Position = 0;
            var img = await Imager.ReadAsync(imageData); // Ensure we're reading imageData, not old content
            
            imageData.Position = 0;
            await assets.InsertOrUpdateAssetVersionMetadataImage(shirtResult.assetVersionId, (int)imageData.Length, img.width, img.height, img.imageFormat, await assets.GenerateImageHash(imageData));
            
            contentId = shirtResult.assetId;
            
            // Clean up the old XML content stream now that we are using the extracted Image stream
            await content.DisposeAsync(); 
            content = null;
        }

        if (robloxDetails.Name == null)
            throw new Exception("Null " + nameof(robloxDetails.Name));
        if (robloxDetails.AssetTypeId == null)
            throw new Exception("Null " + nameof(robloxDetails.AssetTypeId));
            
        var assetResult = await assets.CreateAsset(robloxDetails.Name, robloxDetails.Description, 2500,
            CreatorType.User, 2500, content, robloxDetails.AssetTypeId.Value, Genre.All,
            modState, robloxDetails.Created, robloxDetails.Updated, assetId, disableRender,
            contentId, assetIdOverride: assetId);
        
        if (robloxDetails.AssetTypeId.Value == Type.Image && content != null)
        {
            content.Position = 0;
            var img = await Imager.ReadAsync(content);
            content.Position = 0;
            await assets.InsertOrUpdateAssetVersionMetadataImage(assetResult.assetVersionId, (int)content.Length, img.width, img.height, img.imageFormat, await assets.GenerateImageHash(content));
        }

        await assets.SetItemPrice(assetResult.assetId, price, null);
        await assets.UpdateAssetMarketInfo(assetResult.assetId, isForSale, false, false, null, null);

        // Clean up the final stream
        if (content != null)
            await content.DisposeAsync();

        return new MigrateItem(assetResult.assetId, assetResult.assetVersionId);
    }
}