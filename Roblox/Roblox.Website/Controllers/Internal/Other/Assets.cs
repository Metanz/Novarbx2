using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using Roblox.Exceptions;
using Roblox.Models.Assets;
using Roblox.Website.Middleware;
using BadRequestException = Roblox.Exceptions.BadRequestException;
using MultiGetEntry = Roblox.Dto.Assets.MultiGetEntry;
using Type = Roblox.Models.Assets.Type;
using MVC = Microsoft.AspNetCore.Mvc;
using Roblox.Services.Exceptions;
using Roblox.Website.WebsiteModels.Asset;
using Roblox.Libraries.RobloxApi;
using Roblox.Libraries.Assets;
using Roblox.Website.Lib;
using System.Diagnostics;

namespace Roblox.Website.Controllers 
{
    [MVC.ApiController]
    [MVC.Route("/")]
    public class Assets : ControllerBase 
    {		
        private static readonly HttpClient _proxyClient = new HttpClient() 
        { 
            Timeout = TimeSpan.FromSeconds(10) 
        };
        
        [HttpGet("asset/shader")]
        public async Task<MVC.ActionResult> GetShaderAsset(long id)
        {
            var isMaterialOrShader = BypassControllerMetadata.materialAndShaderAssetIds.Contains(id);
            if (!isMaterialOrShader)
            {
                // Would redirect but that could lead to infinite loop.
                // Just throw instead
                throw new RobloxException(400, 0, "Material/Shader");
            }

            var assetId = id;
            try
            {
                var ourId = await services.assets.GetAssetIdFromRobloxAssetId(assetId);
                assetId = ourId;
            }
            catch (RecordNotFoundException)
            {
                // Doesn't exist yet, so create it
                var migrationResult = await MigrateItem.MigrateItemFromRoblox(assetId.ToString(), false, null, default, new ProductDataResponse()
                {
                    Name = "ShaderConversion" + id,
                    AssetTypeId = Type.Special, // Image
                    Created = DateTime.UtcNow,
                    Updated = DateTime.UtcNow,
                    Description = "ShaderConversion1.0",
                });
                assetId = migrationResult.assetId;
            }
            
            var latestVersion = await services.assets.GetLatestAssetVersion(assetId);
            if (latestVersion.contentUrl is null)
            {
                throw new RobloxException(403, 0, "Forbidden"); // ?
            }

            // FIX 1: Actually fetch the asset content here
            var assetContent = await services.assets.GetAssetContent(latestVersion.contentUrl);

            // These files are large, encourage clients to cache them
            HttpContext.Response.Headers.CacheControl = new CacheControlHeaderValue()
            {
                Public = true,
                MaxAge = TimeSpan.FromDays(360)
            }.ToString();

            if (assetContent != null)
            {
                return base.File(assetContent, "application/binary");
            }

            // FIX 2: Ensure all code paths return a value by throwing if content is null
            throw new BadRequestException(); 
        }

        private bool IsRcc()
        {
            var rccAccessKey = Request.Headers.ContainsKey("accesskey") ? Request.Headers["accesskey"].ToString() : null;
            var isRcc = rccAccessKey == Configuration.RccAuthorization;
            return isRcc;
        }
				
		[HttpGetBypass("game/players/{userId}")]
		public dynamic GetPlayerChatFilter(long userId)
		{
			return new
			{
				ChatFilter = "whitelist"
			};
		}
		
		[HttpGetBypass("/Game/ChatFilter.ashx")]
        public string RCC_GetChatFilter()
        {
            return "True";
        }
		
		private static bool isheaderbad(string headername)
		{
			var badheaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			{
				"Transfer-Encoding",
				"Connection",
				"Keep-Alive",
				"Content-Length",
				"Upgrade",
				"Server"
			};
			
			return badheaders.Contains(headername);
		}

[HttpGetBypass("v2/asset")]
        [HttpGetBypass("v1/asset")]
        [HttpGetBypass("asset")]
        [HttpPostBypass("v1/asset")]
        [HttpPostBypass("asset")]
        public async Task<MVC.ActionResult> GetAssetById(
            [MVC.FromQuery(Name = "id")] long? id = null, 
            [MVC.FromQuery] string? apiKey = null, 
            [MVC.FromQuery(Name = "assetversionid")] long? assetVersionId = null)
        {
            // 1. Resolve ID first from either 'id' or 'assetversionid'
            var effectiveId = id ?? assetVersionId;
            if (!effectiveId.HasValue)
            {
                throw new RobloxException(400, 0, "Asset ID or AssetVersionID is required");
            }

            var assetId = effectiveId.Value;

            // 2. Perform cache check using the resolved assetId
            var CachedRobloxAsset = await GetCachedAsset(assetId);
            if (CachedRobloxAsset != null)
            {
                Console.WriteLine($"[cache] returning cached asset {assetId} from cache");
                return CachedRobloxAsset;
            }
            
            if (apiKey == Configuration.RccAuthorization || apiKey == Configuration.RenderAuthorization)
            {
                var latestVersionSecret = await services.assets.GetLatestAssetVersion(assetId);
                if (latestVersionSecret?.contentUrl == null)
                    throw new RobloxException(400, 0, "Content URL is null");

                var assetContentSecret = await services.assets.GetAssetContent(latestVersionSecret.contentUrl);
                return base.File(assetContentSecret, "application/binary");
            }
            
            var is18OrOver = false;
            if (userSession != null)
            {
                is18OrOver = await services.users.Is18Plus(userSession.userId);
            }

            if (HttpContext.Request.Headers.ContainsKey("RbxTempBypassFor18PlusAssets"))
            {
                is18OrOver = true;
            }
            
            // NEGATIVE CACHE CHECK: Instantly return 404 instead of throwing an exception
            var invalidIdKey = "InvalidAssetIdForConversionV1:" + assetId;
            if (Services.Cache.distributed.StringGetMemory(invalidIdKey) != null)
            {
                return StatusCode(404, "Asset is invalid or does not exist");
            }
            
            var isBotRequest = Request.Headers["bot-auth"].ToString() == Roblox.Configuration.BotAuthorization;
            var isLoggedIn = userSession != null;
            var encryptionEnabled = !isBotRequest;

            var isMaterialOrShader = BypassControllerMetadata.materialAndShaderAssetIds.Contains(assetId);
            if (isMaterialOrShader)
            {
                return new MVC.RedirectResult("/asset/shader?id=" + assetId);
            }

            var isRcc = IsRcc();
            if (isRcc)
                encryptionEnabled = false;
        #if DEBUG
            encryptionEnabled = false;
        #endif

            MultiGetEntry details;
            try
            {
                details = await services.assets.GetAssetCatalogInfo(assetId);
            }
            catch (RecordNotFoundException)
            {
                try
                {
                    var ourId = await services.assets.GetAssetIdFromRobloxAssetId(assetId);
                    assetId = ourId;
                }
                catch (RecordNotFoundException)
                {       
                    var pxyurl = $"http://assetdelivery.novarbx.cc/asset/?id={assetId}";

                    using var httpClient = new HttpClient();
                    httpClient.Timeout = TimeSpan.FromSeconds(10);
                    
                    try
                    {
                        var stopwatch = Stopwatch.StartNew();
                        var response = await _proxyClient.GetAsync(pxyurl, HttpCompletionOption.ResponseHeadersRead);
                        stopwatch.Stop();
                        
                        if (response.IsSuccessStatusCode)
                        {
                            var content = await response.Content.ReadAsByteArrayAsync();
                            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

                            Response.Headers.Clear();

                            foreach (var header in response.Headers)
                            {
                                if (!isheaderbad(header.Key))
                                {
                                    Response.Headers[header.Key] = header.Value.ToArray();
                                }
                            }

                            Response.Headers["Content-Type"] = contentType;
                            
                            await CacheAsset(assetId, content, contentType);
                            return base.File(content, contentType);
                        }
                        else
                        {
                            // NEGATIVE CACHE SAVE: 2 arguments so it compiles
                            Services.Cache.distributed.StringSet(invalidIdKey, "1");
                            return StatusCode((int)response.StatusCode, response.ReasonPhrase);
                        }
                    }
                    catch (Exception ex)
                    {       
                        // NEGATIVE CACHE SAVE: 2 arguments so it compiles
                        Services.Cache.distributed.StringSet(invalidIdKey, "1");

                        if (ex is TaskCanceledException)
                        {
                            return StatusCode(408, "Gateway Timeout");
                        }
                        
                        return StatusCode(502, "Bad Gateway");
                    }
                }
                details = await services.assets.GetAssetCatalogInfo(assetId);
            }

            if (details.is18Plus && !isRcc && !isBotRequest && !is18OrOver)
                throw new RobloxException(400, 0, "AssetTemporarilyUnavailable");
            if (details.moderationStatus != ModerationStatus.ReviewApproved && !isRcc && !isBotRequest)
                throw new RobloxException(403, 0, "Asset is not approved");
            
            var latestVersion = await services.assets.GetLatestAssetVersion(assetId);
            Stream? assetContent = null;
            Console.WriteLine($"[debug] assetId={assetId}, assetType={details.assetType}, moderation={details.moderationStatus}, isRcc={isRcc}, isBot={isBotRequest}, is18={is18OrOver}");
            
            switch (details.assetType)
            {
                case Roblox.Models.Assets.Type.TeeShirt:
                    var teeShirtData = ContentFormatters.GetTeeShirt(latestVersion.contentId);
                    return new MVC.FileContentResult(Encoding.UTF8.GetBytes(teeShirtData), "application/binary");

                case Models.Assets.Type.Shirt:
                    var shirtData = ContentFormatters.GetShirt(latestVersion.contentId);
                    return new MVC.FileContentResult(Encoding.UTF8.GetBytes(shirtData), "application/binary");

                case Models.Assets.Type.Pants:
                    var pantsData = ContentFormatters.GetPants(latestVersion.contentId);
                    return new MVC.FileContentResult(Encoding.UTF8.GetBytes(pantsData), "application/binary");

                case Models.Assets.Type.Image:
                case Models.Assets.Type.Special:
                    if (latestVersion.contentUrl != null)
                        assetContent = await services.assets.GetAssetContent(latestVersion.contentUrl);
                    break;

                case Models.Assets.Type.Audio:
                case Models.Assets.Type.Mesh:
                case Models.Assets.Type.Hat:
                case Models.Assets.Type.Model:
                case Models.Assets.Type.Decal:
                case Models.Assets.Type.Head:
                case Models.Assets.Type.Face:
                case Models.Assets.Type.Gear:
                case Models.Assets.Type.Badge:
                case Models.Assets.Type.Animation:
                case Models.Assets.Type.Torso:
                case Models.Assets.Type.RightArm:
                case Models.Assets.Type.LeftArm:
                case Models.Assets.Type.RightLeg:
                case Models.Assets.Type.LeftLeg:
                case Models.Assets.Type.Package:
                case Models.Assets.Type.GamePass:
                case Models.Assets.Type.Plugin:
                case Models.Assets.Type.MeshPart:
                case Models.Assets.Type.HairAccessory:
                case Models.Assets.Type.FaceAccessory:
                case Models.Assets.Type.NeckAccessory:
                case Models.Assets.Type.ShoulderAccessory:
                case Models.Assets.Type.FrontAccessory:
                case Models.Assets.Type.BackAccessory:
                case Models.Assets.Type.WaistAccessory:
                case Models.Assets.Type.ClimbAnimation:
                case Models.Assets.Type.DeathAnimation:
                case Models.Assets.Type.FallAnimation:
                case Models.Assets.Type.IdleAnimation:
                case Models.Assets.Type.JumpAnimation:
                case Models.Assets.Type.RunAnimation:
                case Models.Assets.Type.SwimAnimation:
                case Models.Assets.Type.WalkAnimation:
                case Models.Assets.Type.PoseAnimation:
                case Models.Assets.Type.EmoteAnimation:
                case Models.Assets.Type.SolidModel:
                    if (latestVersion.contentUrl is null)
                        throw new RobloxException(400, 0, "Content URL is null");

                    if (details.assetType == Models.Assets.Type.Audio)
                    {
                        assetContent = await services.assets.GetAudioContentAsWav(assetId, latestVersion.contentUrl);
                    }
                    else
                    {
                        assetContent = await services.assets.GetAssetContent(latestVersion.contentUrl);
                    }
                    break;

                default:
                    var ok = false;
                    if (isRcc)
                    {
                        encryptionEnabled = false;
                        ok = true;
                        var placeIdHeader = Request.Headers["roblox-place-id"].ToString();
                        long placeId = 0;
                        if (!string.IsNullOrEmpty(placeIdHeader))
                        {
                            long.TryParse(placeIdHeader, out placeId);
                        }
                        ok = (placeId == assetId);

                        if (!ok && details.assetType == Models.Assets.Type.Place && placeId == 0)
                        {
                            ok = true;
                        }

                        if (!ok)
                        {
                            var placeDetails = await services.assets.GetAssetCatalogInfo(placeId);
                            if (placeDetails.creatorType == details.creatorType &&
                                placeDetails.creatorTargetId == details.creatorTargetId)
                            {
                                ok = true;
                            }
                        }
                    }
                    else
                    {
                        if (userSession != null)
                        {
                            ok = await services.assets.CanUserModifyItem(assetId, userSession.userId);
                            if (!ok)
                            {
                                ok = (details.creatorType == CreatorType.User && details.creatorTargetId == 1);
                            }
        #if DEBUG
                            if (await services.users.IsUserStaff(userSession.userId))
                            {
                                ok = true;
                            }
        #endif
                            if (ok)
                            {
                                encryptionEnabled = false;
                            }
                        }
                    }

                    if (ok && latestVersion.contentUrl != null)
                    {
                        assetContent = await services.assets.GetAssetContent(latestVersion.contentUrl);
                    }
                    break;
            }

            if (assetContent != null)
            {
                return base.File(assetContent, "application/binary");
            }

            Console.WriteLine("[info] got BadRequest on /asset/ endpoint");
            throw new BadRequestException();
        }

        private static bool _cacheDirCreated = false;
		private async Task CacheAsset(long assetId, byte[] content, string contentType)
        {
            try
            {
                var CacheDIR = Path.Combine(Directory.GetCurrentDirectory(), "AssetCache");
                
                if (!_cacheDirCreated)
                {
                    Directory.CreateDirectory(CacheDIR); // Does nothing if it already exists
                    _cacheDirCreated = true;
                }
				
				var Cache = Path.Combine(CacheDIR, $"{assetId}.cache");
				var Meta = Path.Combine(CacheDIR, $"{assetId}.meta");
				
				await System.IO.File.WriteAllBytesAsync(Cache, content);
				
				var MetaData = new
				{
					ContentType = contentType,
					CachedAt = DateTime.UtcNow,
					AssetId = assetId
				};
				await System.IO.File.WriteAllTextAsync(Meta, System.Text.Json.JsonSerializer.Serialize(MetaData));
				
				Console.WriteLine($"[cache] cached asset {assetId}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[cache] failed to cache asset {assetId}: {ex.Message}");
			}
		}
		
		private async Task<MVC.ActionResult?> GetCachedAsset(long assetId)
        {
            try
            {
                var CacheDIR = Path.Combine(Directory.GetCurrentDirectory(), "AssetCache");
                var Cache = Path.Combine(CacheDIR, $"{assetId}.cache");
                var Meta = Path.Combine(CacheDIR, $"{assetId}.meta");
                
                if (System.IO.File.Exists(Cache) && System.IO.File.Exists(Meta))
                {
                    // Just read the small meta string, not the whole asset file
                    var MetaJSON = await System.IO.File.ReadAllTextAsync(Meta);
                    var MetaData = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(MetaJSON);
                    string contentType = MetaData.TryGetProperty("ContentType", out var ct)
                        ? ct.GetString() ?? "application/octet-stream"
                        : "application/octet-stream";

                    // Add the cache headers here too!
                    HttpContext.Response.Headers.CacheControl = new CacheControlHeaderValue()
                    {
                        Public = true,
                        MaxAge = TimeSpan.FromDays(360)
                    }.ToString();

                    // Let ASP.NET stream the file from disk efficiently
                    return PhysicalFile(Cache, contentType);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[cache] error getting cached asset {assetId}: {ex.Message}");
            }
            
            return null;
        }
				
		public class BatchAssetRequest
		{
			public long assetId { get; set; }
			public string assetType { get; set; }
			public string requestId { get; set; }
		}
		
		[HttpPostBypass("asset/batch")]
        [HttpPostBypass("v1/assets/batch")]
        public async Task<MVC.IActionResult> AssetBatch()
        {
            List<BatchAssetRequest> requestData;
            bool isGzip = Request.Headers["Content-Encoding"].ToString() == "gzip";
            
            if (isGzip)
            {
                using (var decompressedStream = new MemoryStream())
                {
                    using (var requestStream = Request.Body)
                    {
                        using (var gzipStream = new GZipStream(requestStream, CompressionMode.Decompress))
                        {
                            await gzipStream.CopyToAsync(decompressedStream);
                        }
                    }
                    decompressedStream.Seek(0, SeekOrigin.Begin);

                    using (var reader = new StreamReader(decompressedStream, Encoding.UTF8))
                    {
                        var json = await reader.ReadToEndAsync();
                        Console.WriteLine(json);
                        requestData = Newtonsoft.Json.JsonConvert.DeserializeObject<List<BatchAssetRequest>>(json);
                    }
                }
            }
            else
            {
                using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
                {
                    var json = await reader.ReadToEndAsync();
                    Console.WriteLine(json);
                    requestData = Newtonsoft.Json.JsonConvert.DeserializeObject<List<BatchAssetRequest>>(json);
                }
            }
            if (requestData == null)
            {
                throw new BadRequestException();
            }
            var assetReturnInfo = new List<object>();
            foreach (var request in requestData)
            {
                Console.WriteLine(request.assetId);
                assetReturnInfo.Add(new
                {
                    Location = $"{Configuration.BaseUrl}/v1/asset?id={request.assetId}",
                    RequestId = request.requestId,
                    IsHashDynamic = true,
                    IsCopyrightProtected = true, 
                    IsArchived = false,
                });
            }

            return Content(Newtonsoft.Json.JsonConvert.SerializeObject(assetReturnInfo), "application/json");
        }
	}
}	