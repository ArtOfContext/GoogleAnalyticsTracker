using System;
using System.Collections.Generic;
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
            // read any "custom variables" that have been set on the request
            object customVariablesObj;
            filterContext.Request.Properties.TryGetValue("AnalyticsCustomVariables", out customVariablesObj);
            var customVariablesDictionary = customVariablesObj as Dictionary<string, string> ?? new Dictionary<string, string>();

            int position = 1;

            Tracker.ClearCustomVariables();

            foreach (var key in customVariablesDictionary.Keys)
            {
                Tracker.SetCustomVariable(position++, key, customVariablesDictionary[key]);

                // only 5 custom variables allowed in Google Analytics
                if (position > 5)
                {
                    break;
                }
            }
        }
    }
}