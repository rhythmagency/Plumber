﻿using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Web.Http;
using Umbraco.Core;
using Umbraco.Core.Persistence;
using Umbraco.Web.WebApi;
using Workflow.Models;

namespace Workflow.Api
{
    public class ConfigController : UmbracoAuthorizedApiController
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private Database db = ApplicationContext.Current.DatabaseContext.Database;

        /// <summary>
        /// Persist the workflow approval config
        /// </summary>
        /// <param name="group"></param>
        /// <returns></returns>
        [System.Web.Http.HttpPost]
        public IHttpActionResult Save(List<UserGroupPermissionsPoco> model)
        {
            try
            {
                if (model.Where(p => p.ContentTypeId > 0).Any())
                {
                    // set defaults for doctype - delete all previous
                    db.Execute("DELETE FROM WorkflowUserGroupPermissions WHERE ContentTypeId != 0");
                    db.BulkInsertRecords<UserGroupPermissionsPoco>(model);
                }
                else
                {
                    db.Execute("DELETE FROM WorkflowUserGroupPermissions WHERE NodeId = @0", model.First().NodeId);
                    db.BulkInsertRecords<UserGroupPermissionsPoco>(model);            
                }
            }
            catch (Exception ex)
            {
                var msg = "Error saving config. " + ex.Message;
                log.Error(msg, ex);
                return Content(HttpStatusCode.InternalServerError, ViewHelpers.ApiException(ex, msg));
            }

            return Ok();
        }
    }
}