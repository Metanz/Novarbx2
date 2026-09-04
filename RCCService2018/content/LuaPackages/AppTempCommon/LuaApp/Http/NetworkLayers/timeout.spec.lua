return function()
	local timeout = require(script.Parent.timeout)

	local CorePackages = game:GetService("CorePackages")
	local AppTempCommon = CorePackages.AppTempCommon

	local Promise = require(AppTempCommon.LuaApp.Promise)
	local HttpError = require(AppTempCommon.LuaApp.Http.HttpError)
	local HttpResponse = require(AppTempCommon.LuaApp.Http.HttpResponse)
	local StatusCodes = require(AppTempCommon.LuaApp.Http.StatusCodes)
	local MockRequest = require(AppTempCommon.LuaApp.TestHelpers.MockRequest)

	it("should return a function", function()
		local request = MockRequest.simpleSuccessRequest("timeout.spec - simple constructor test")
		request = timeout(request)

		expect(request).to.be.ok()
		expect(type(request)).to.equal("function")
	end)

	it("should pass the resolved value through", function()
		local testUrl = "fakeUrl"
		local testResponse = "fakeResponse"
		local testResponseTime = 0
		local testStatusCode = StatusCodes.OK

		local timeBeforeBail = 0.1
		local request = function(url, requestMethod, options)
			return Promise.resolve(HttpResponse.new(url, testResponse, testResponseTime, testStatusCode))
		end
		request = timeout(request, timeBeforeBail)

		local httpPromise = request(testUrl, "GET")
		httpPromise:resolve(function(hr)
			expect(hr.requestUrl).to.equal(testUrl)
			expect(hr.responseTimeMs).to.equal(testResponseTime)
			expect(hr.responseCode).to.equal(testStatusCode)
			expect(hr.responseBody).to.equal(testResponse)
		end)
	end)

	describe("the retry behavior", function()
		HACK_NO_XPCALL()

		it("should return a rejected promise if a request takes too long", function()
			local errUpval
			local timeBeforeBail = 0.01
			local request = function(url, requestMethod, options)
				return Promise.new(function(resolve, reject)
					-- don't resolve or reject, so timeout will fire
				end)
			end
			request = timeout(request, timeBeforeBail)

			local httpPromise = request("fakeUrl", "GET")
			httpPromise:catch(function(httpError)
				errUpval = httpError
			end)

			wait()

			expect(errUpval.kind).to.equal(HttpError.Kind.LuaTimeout)
			expect(errUpval.targetUrl).to.equal("fakeUrl")
		end)
	end)
end