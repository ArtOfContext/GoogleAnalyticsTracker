using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using GoogleAnalyticsTracker.Core;

namespace GoogleAnalyticsTracker.WebApi2 {
    public class ActionTrackingAttribute
            : AsyncActionFilterAttribute
    {
        private Func<HttpActionExecutedContext, bool> _isTrackableAction;

        public Tracker Tracker { get; set; }

        public Func<HttpActionExecutedContext, bool> IsTrackableAction
        {
            get
            {
                if (_isTrackableAction != null)
                {
                    return _isTrackableAction;
                }
                return action => true;
            }
            set { _isTrackableAction = value; }
        }

        public string ActionDescription { get; set; }
        public string ActionUrl { get; set; }

        public ActionTrackingAttribute()
            : this(null, null, null, null)
        {
        }

        public ActionTrackingAttribute(string trackingAccount, string trackingDomain)
            : this(trackingAccount, trackingDomain, null, null)
        {
        }

        public ActionTrackingAttribute(string trackingAccount)
            : this(trackingAccount, null, null, null)
        {
        }

        public ActionTrackingAttribute(string trackingAccount, string trackingDomain, string actionDescription, string actionUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(trackingDomain) && System.Web.HttpContext.Current != null && System.Web.HttpContext.Current.Request != null)
                {
                    trackingDomain = System.Web.HttpContext.Current.Request.Url.Host;
                }
            }
            catch
            {
                // intended
            }

            Tracker = new Tracker(trackingAccount, trackingDomain, new CookieBasedAnalyticsSession(), new AspNetWebApiTrackerEnvironment());
            ActionDescription = actionDescription;
            ActionUrl = actionUrl;
        }

        public ActionTrackingAttribute(Tracker tracker)
            : this(tracker, action => true)
        {
        }

        public ActionTrackingAttribute(Tracker tracker, Func<HttpActionExecutedContext, bool> isTrackableAction)
        {
            Tracker = tracker;
            IsTrackableAction = isTrackableAction;
        }

        public ActionTrackingAttribute(string trackingAccount, string trackingDomain, Func<HttpActionExecutedContext, bool> isTrackableAction)
        {
            Tracker = new Tracker(trackingAccount, trackingDomain, new CookieBasedAnalyticsSession(), new AspNetWebApiTrackerEnvironment());
            IsTrackableAction = isTrackableAction;
        }

        public async override Task OnActionExecutedAsync(HttpActionExecutedContext actionExecutedContext, CancellationToken cancellationToken)
        {
            if (IsTrackableAction(actionExecutedContext))
            {
                var requireRequestAndResponse = Tracker.AnalyticsSession as IRequireRequestAndResponse;
                if (requireRequestAndResponse != null)
                {
                    requireRequestAndResponse.SetRequestAndResponse(actionExecutedContext.Request, actionExecutedContext.Response);
                }

                await OnTrackingAction(actionExecutedContext);
            }
        }

        public virtual string BuildCurrentActionName(HttpActionExecutedContext filterContext)
        {
            return ActionDescription ??
                         filterContext.ActionContext.ControllerContext.ControllerDescriptor.ControllerName + " - " +
                         filterContext.ActionContext.ActionDescriptor.ActionName;
        }

        public virtual string BuildCurrentActionUrl(HttpActionExecutedContext filterContext)
        {
            var request = filterContext.Request;

            return ActionUrl ?? (request.RequestUri != null ? request.RequestUri.PathAndQuery : "");
        }

        public virtual async Task<TrackingResult> OnTrackingAction(HttpActionExecutedContext filterContext)
        {
            SetRequestPropertiesCustomVariables(filterContext);

            return await Tracker.TrackPageViewAsync(
                filterContext.Request,
                BuildCurrentActionName(filterContext),
                BuildCurrentActionUrl(filterContext));
        }

        private void SetRequestPropertiesCustomVariables(HttpActionExecutedContext filterContext)
        {
            int position = 1;
            var customVars = new List<Tuple<string, string>>(5);

            // read any "custom variables" that have been set on the request
            object customVariablesObj;
            filterContext.Request.Properties.TryGetValue("AnalyticsCustomVariables", out customVariablesObj);
            var requestCustomVariablesDict = customVariablesObj as Dictionary<string, string> ?? new Dictionary<string, string>();

            // also will take custom variables from the controller action arguments
            Dictionary<string, object> actionArgumentsDict = filterContext.ActionContext.ActionArguments;

            customVars.AddRange(requestCustomVariablesDict.Keys.Select(key =>
                new Tuple<string, string>(key, (requestCustomVariablesDict[key] ?? "").ToString(CultureInfo.InvariantCulture))));

            customVars.AddRange(actionArgumentsDict.Keys.Select(key =>
                new Tuple<string, string>(key, (actionArgumentsDict[key] ?? "").ToString())));

            AddPlaceholdersForMissingRequestCustomVariables(requestCustomVariablesDict, actionArgumentsDict, customVars);

            Tracker.ClearCustomVariables();

            foreach (var customVarPair in customVars.OrderBy(tuple => tuple.Item1))
            {
                Tracker.SetCustomVariable(position++, customVarPair.Item1, customVarPair.Item2);

                // only 5 custom variables allowed in Google Analytics
                if (position > 5)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// In the case that custom variables were recorded on the request.Properties inside controller actions
        ///   and then the response was cached, and then later the response is served from cache:
        /// Since came from cache, the action was not run this time around and custom vars were not recorded inside
        ///   controller action.  We would like custom variables to always be logged in the same google analytics slot
        ///   so record some placeholder custom variables
        /// </summary>
        private void AddPlaceholdersForMissingRequestCustomVariables(
            Dictionary<string, string> requestCustomVariablesDict,
            Dictionary<string, object> actionArgumentsDict,
            List<Tuple<string, string>> customVars)
        {
            const string idKeySuffix = "Id";
            const string nameKeySuffix = "Name";

            var requestKeys = requestCustomVariablesDict.Keys;
            var actionKeys = actionArgumentsDict.Keys;

            // there should be an xyzName variable for each xyzId variable

            foreach (var actionKey in actionKeys)
            {
                if (actionKey.EndsWith(idKeySuffix))
                {
                    string nameKey = actionKey.Substring(0, actionKey.Length - idKeySuffix.Length) + nameKeySuffix;
                    if (requestKeys.All(s => s != nameKey))
                    {
                        customVars.Add(new Tuple<string, string>(nameKey + "Placeholder", "0"));
                    }
                }
            }
        }
    }
}