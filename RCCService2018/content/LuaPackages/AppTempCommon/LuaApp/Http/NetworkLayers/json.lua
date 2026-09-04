--[[
	Given an http promise, attempt to parse the resulting response body from JSON to a table
]]
local CorePackages = game:GetService("CorePackages")
local HttpService = game:GetService("HttpService")

local AppTempCommon = CorePackages.AppTempCommon

local Promise = require(AppTempCommon.LuaApp.Promise)
local HttpError = require(AppTempCommon.LuaApp.Http.HttpError)
local HttpResponse = require(AppTempCommon.LuaApp.Http.HttpResponse)

-- requestFunc : (function<promise<HttpResponse>>(url, requestMethod, options))
-- RETURNS : (function<promise<HttpResponse>>(url, requestMethod, options)) replaces the response's body with a table
return function(requestFunc)
	return function(url, requestMethod, options)
		local httpPromise = requestFunc(url, requestMethod, options):andThen(function(response)
			local ok, result = pcall(HttpService.JSONDecode, HttpService, response.responseBody)

			if not ok then
				local errMsg = string.format("Cannot parse : %s", response.responseBody or "nil")
				return Promise.reject(HttpError.new(url, HttpError.Kind.InvalidJson, errMsg))
			end

			return HttpResponse.new(
				response.requestUrl,
				result, -- the response body is now a table, not a string
				response.responseTimeMs,
				response.responseCode)
		end)

		return httpPromise
	end
end