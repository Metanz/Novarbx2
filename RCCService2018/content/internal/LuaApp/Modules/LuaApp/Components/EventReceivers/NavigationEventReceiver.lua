local CoreGui = game:GetService("CoreGui")
local HttpService = game:GetService("HttpService")

local Modules = CoreGui.RobloxGui.Modules

local Roact = require(Modules.Common.Roact)
local RoactRodux = require(Modules.Common.RoactRodux)

local AppPage = require(Modules.LuaApp.AppPage)
local NavigateToRoute = require(Modules.LuaApp.Thunks.NavigateToRoute)
local NavigateDown = require(Modules.LuaApp.Thunks.NavigateDown)

local NavigationEventReceiver = Roact.Component:extend("NavigationEventReceiver")

function NavigationEventReceiver:handleNavigationEvent(detail)
	local eventDetails = HttpService:JSONDecode(detail)
	if eventDetails.appName == AppPage.ShareGameToChat then
		self.props.navigateDown({
			name = AppPage.ShareGameToChat,
			detail = eventDetails.parameters.placeId,
		})
	elseif eventDetails.appName == AppPage.Chat then
		self.props.setPage({
			name = AppPage.Chat,
			detail = eventDetails.parameters and eventDetails.parameters.conversationId,
		})
	else
		self.props.setPage({
			name = AppPage[eventDetails.appName] or AppPage.None,
		})
	end
end

function NavigationEventReceiver:init()
	local robloxEventReceiver = self.props.RobloxEventReceiver

	self.tokens = {
		robloxEventReceiver:observeEvent("Navigations", "Destination", function(detail)
			self:handleNavigationEvent(detail)
		end),
		robloxEventReceiver:observeEvent("Navigations", "Reload",  function(detail)
			self:handleNavigationEvent(detail)
		end)
	}
end

function NavigationEventReceiver:render()
end

function NavigationEventReceiver:willUnmount()
	for _, connection in pairs(self.tokens) do
		connection:Disconnect()
	end
end

return RoactRodux.UNSTABLE_connect2(
	nil,
	function(dispatch)
		return {
			setPage = function(page)
				return dispatch(NavigateToRoute({ page }))
			end,
			navigateDown = function(page)
				return dispatch(NavigateDown(page))
			end,
		}
	end
)(NavigationEventReceiver)