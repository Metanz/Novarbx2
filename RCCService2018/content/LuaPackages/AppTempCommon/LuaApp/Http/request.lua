--[[
	Provides a configured networking stack to store in the ServiceProvider
]]--
local CorePackages = game:GetService("CorePackages")
local AppTempCommon = CorePackages.AppTempCommon

local json = require(AppTempCommon.LuaApp.Http.NetworkLayers.json)
local requestDataModel = require(AppTempCommon.LuaApp.Http.NetworkLayers.requestDataModel)
local retry = require(AppTempCommon.LuaApp.Http.NetworkLayers.retry)
local timeout = require(AppTempCommon.LuaApp.Http.NetworkLayers.timeout)

-- construct the networking stack
local request = requestDataModel()
request = timeout(request) -- the timeout default configuration is fine
request = retry(request) -- the retry default configuration is fine
request = json(request)

-- RETURNS : function<promise<HttpResponse>>(url, requestMethod, args)
return request