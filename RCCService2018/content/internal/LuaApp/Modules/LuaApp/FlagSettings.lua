local NotificationService = game:GetService("NotificationService")

local FlagSettings = {}

local function IsRunningInStudio()
	return game:GetService("RunService"):IsStudio()
end

function FlagSettings.IsLuaAppStarterScriptEnabled(platform)
	if IsRunningInStudio() then
		return true
	end

	if platform == Enum.Platform.IOS then
		return settings():GetFFlag("UseLuaAppStarterScriptOniOS")
	elseif platform == Enum.Platform.Android then
		return FlagSettings.IsLuaHomePageEnabled(platform) or FlagSettings.IsLuaGamesPageEnabled(platform)
	else
		return false
	end
end

function FlagSettings.IsLuaHomePageEnabled(platform)
	if IsRunningInStudio() then
		return true
	end

	if platform == Enum.Platform.IOS then
		return NotificationService.IsLuaHomePageEnabled
	elseif platform == Enum.Platform.Android then
		return settings():GetFFlag("UseLuaHomePageOnAndroidPhone") or settings():GetFFlag("UseLuaHomePageOnAndroidTablet")
	else
		return false
	end
end

function FlagSettings.IsLuaGamesPageEnabled(platform)
	if IsRunningInStudio() then
		return true
	end

	if platform == Enum.Platform.IOS then
		return NotificationService.IsLuaGamesPageEnabled
	elseif platform == Enum.Platform.Android then
		return settings():GetFFlag("UseLuaGamesPageOnAndroidPhone") or settings():GetFFlag("UseLuaGamesPageOnAndroidTablet")
	else
		return false
	end
end

function FlagSettings.IsLuaBottomBarEnabled()
	return IsRunningInStudio()
end

function FlagSettings.IsLuaAppFriendshipCreatedSignalREnabled()
	return settings():GetFFlag("LuaAppFriendshipCreatedSignalREnabled")
end

function FlagSettings.IsLuaAppDeterminingFormFactorAndPlatform()
	return settings():GetFFlag("UseLuaAppStarterScriptOniOS") and settings():GetFFlag("EnableLuaAppFormFactorAndPlatform")
end

function FlagSettings.IsLoadingHUDOniOSEnabledForGameShare()
	return settings():GetFFlag("UseLuaAppStarterScriptOniOS") and settings():GetFFlag("EnableLoadingHUDOniOSForGameShare")
end

function FlagSettings.IsPeopleListV1Enabled()
	return settings():GetFFlag("LuaAppPeopleListV1")
end

function FlagSettings:UseCppTextTruncation()
	return settings():GetFFlag("TextTruncationEnabled")
end

return FlagSettings