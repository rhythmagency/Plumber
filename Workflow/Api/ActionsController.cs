﻿using log4net;
using System;
using System.Linq;
using System.Reflection;
using System.Web.Http;
using Umbraco.Web.WebApi;
using Workflow.Models;
using Workflow.Helpers;
using Workflow.Processes;

namespace Workflow.Api
{
    /// <summary>
    /// WebAPI methods for generating the user workflow dashboard
    /// </summary>
    [RoutePrefix("umbraco/backoffice/api/workflow/actions")]
    public class ActionsController : UmbracoAuthorizedApiController
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly PocoRepository Pr = new PocoRepository();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("initiate")]
        public IHttpActionResult InitiateWorkflow(InitiateWorkflowModel model)
        {
            try
            {
                WorkflowApprovalProcess process;

                if (model.Publish)
                {
                    process = new DocumentPublishProcess();
                }
                else
                {
                    process = new DocumentUnpublishProcess();
                }

                var instance = process.InitiateWorkflow(int.Parse(model.NodeId), Utility.GetCurrentUser().Id, model.Comment);

                var msg = string.Empty;

                switch (instance.WorkflowStatus)
                {
                    case WorkflowStatus.PendingApproval:
                        msg = "Page submitted for " + (model.Publish ? "publish" : "unpublish") + " approval.";
                        break;
                    case WorkflowStatus.Approved:
                        msg = (model.Publish ? "Publish" : "Unpublish") + " workflow complete.";

                        if (instance.ScheduledDate.HasValue)
                        {
                            msg += " Page scheduled for publishing at " + instance.ScheduledDate.ToString();
                        }

                        break;
                }

                return Json(new
                {
                    message = msg,
                    status = 200
                }, ViewHelpers.CamelCase);

            }
            catch (Exception e)
            {
                return Json(new
                {
                    message = ViewHelpers.ApiException(e),
                    status = 500
                }, ViewHelpers.CamelCase);
            }
        }


        /// <summary>
        /// Processes the workflow task for the given taskdata
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("approve")]
        public IHttpActionResult ApproveWorkflowTask(TaskData model)
        {
            var instance = GetInstance(model.InstanceGuid);

            try
            {
                WorkflowApprovalProcess process = GetProcess(instance.Type);

                instance = process.ActionWorkflow(
                    instance,
                    WorkflowAction.Approve,
                    Utility.GetCurrentUser().Id,
                    model.Comment
                );

                var msg = string.Empty;

                switch (instance.WorkflowStatus)
                {
                    case WorkflowStatus.PendingApproval:
                        msg = "Approval completed successfully. Page will be " + instance.TypeDescriptionPastTense.ToLower() + " following workflow completion.";
                        break;
                    case WorkflowStatus.Approved:
                        msg = "Workflow approved successfully.";

                        if (instance.ScheduledDate.HasValue)
                        {
                            msg += " Page scheduled for " + instance.TypeDescription + " at " + instance.ScheduledDate.ToString();
                        }
                        else
                        {
                            msg += " Page has been " + instance.TypeDescriptionPastTense.ToLower();
                        }
                        break;
                }

                return Json(new
                {
                    message = msg,
                    status = 200
                }, ViewHelpers.CamelCase);
            }
            catch (Exception ex)
            {
                var msg = "An error occurred processing the approval: " + ex.Message + ex.StackTrace;
                Log.Error(msg + " for workflow " + instance.Id, ex);
                return Json(new
                {
                    message = msg,
                    status = 500
                }, ViewHelpers.CamelCase);
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("reject")]
        public IHttpActionResult RejectWorkflowTask(TaskData model)
        {
            var instance = GetInstance(model.InstanceGuid);

            try
            {
                WorkflowApprovalProcess process = GetProcess(instance.Type);

                instance = process.ActionWorkflow(
                    instance,
                    WorkflowAction.Reject,
                    Utility.GetCurrentUser().Id,
                    model.Comment
                );

                return Json(new
                {
                    message = instance.TypeDescription + " request has been rejected.",
                    status = 200
                }, ViewHelpers.CamelCase);
            }
            catch (Exception ex)
            {
                var msg = "An error occurred rejecting the workflow: " + ex.Message + ex.StackTrace;
                Log.Error(msg + " for workflow " + instance.Id, ex);

                return Json(new
                {
                    message = msg,
                    status = 500
                }, ViewHelpers.CamelCase);
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("cancel")]
        public IHttpActionResult CancelWorkflowTask(TaskData model)
        {
            var instance = GetInstance(model.InstanceGuid);

            try
            {
                WorkflowApprovalProcess process = GetProcess(instance.Type);

                instance = process.CancelWorkflow(
                    instance,
                    Utility.GetCurrentUser().Id,
                    model.Comment
                );

                return Json(new
                {
                    status = 200,
                    message = instance.TypeDescription + " workflow cancelled"
                }, ViewHelpers.CamelCase);
            }
            catch (Exception ex)
            {
                var msg = "An error occurred cancelling the workflow: " + ex.Message + ex.StackTrace;
                Log.Error(msg + " for workflow " + instance.Id, ex);
                return Json(new
                {
                    status = 500,
                    message = msg
                }, ViewHelpers.CamelCase);
            }
        }

        /// <summary>
        /// Endpoint for resubmitting a workflow when the previous task was rejected
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("resubmit")]
        public IHttpActionResult ResubmitWorkflowTask(TaskData model)
        {
            WorkflowInstancePoco instance = GetInstance(model.InstanceGuid);

            try
            {
                WorkflowApprovalProcess process = GetProcess(instance.Type);

                instance = process.ResubmitWorkflow(
                    instance,
                    Utility.GetCurrentUser().Id,
                    model.Comment
                );

                return Json(new
                {
                    message = "Changes resubmitted successfully. Page will be " + instance.TypeDescriptionPastTense.ToLower() + " following workflow completion.",
                    status = 200
                }, ViewHelpers.CamelCase);
            }
            catch (Exception ex)
            {
                string msg = "An error occurred processing the approval: " + ex.Message + ex.StackTrace;
                Log.Error(msg + " for workflow " + instance.Id, ex);

                return Json(new
                {
                    message = msg,
                    status = 500
                }, ViewHelpers.CamelCase);
            }
        }

        #region Private methods

        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private static dynamic GetProcess(int type)
        {
            if ((WorkflowType)type == WorkflowType.Publish)
            {
                return new DocumentPublishProcess();
            }
            return new DocumentUnpublishProcess();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="instanceGuid"></param>
        /// <returns></returns>
        private static WorkflowInstancePoco GetInstance(Guid instanceGuid)
        {
            var instance = Pr.InstanceByGuid(instanceGuid);
            instance.SetScheduledDate();

            // TODO -> fix this
            var tasks = Pr.TasksAndGroupByInstanceId(instance.Guid);

            if (tasks.Any())
            {
                instance.TaskInstances = tasks;
            }

            return instance;
        }

        #endregion
    }
}