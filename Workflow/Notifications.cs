﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Reflection;
using System.Threading.Tasks.Dataflow;
using log4net;
using Workflow.Helpers;
using Workflow.Models;

namespace Workflow
{
    public static class Notifications
    {
        private static readonly PocoRepository Pr = new PocoRepository();
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// TODO: these should come from a config file rather than static strings...
        /// </summary>
        private const string EmailApprovalRequestString = "Dear {0},<br/><br/>Please review the following page for {5} approval: <a href=\"{1}\">{2}</a><br/><br/>Comment: {3}<br/><br/>Thanks,<br/>{4}";
        private const string EmailApprovedString = "Dear {0},<br/><br/>The following document's workflow has been approved and the document {3}: <a href=\"{1}\">{2}</a><br/>";
        private const string EmailRejectedString = "Dear {0},<br/><br/>The {5} workflow was rejected by {4}: <a href=\"{1}\">{2}</a><br/><br/>Comment: {3}";
        private const string EmailCancelledString = "Dear {0},<br/><br/>{1} workflow has been cancelled for the following page: <a href=\"{2}\">{3}</a> by {4}.<br/><br/>Reason: {5}.";

        /// <summary>
        /// Sends an email notification out for the workflow process
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="emailType">the type of email to be sent</param>
        public static void Send(WorkflowInstancePoco instance, EmailType emailType)
        {
            bool? doSend = Pr.GetSettings().SendNotifications;
            if (doSend != true) return;

            try
            {
                var docTitle = instance.Node.Name;
                var docUrl = UrlHelpers.GetFullyQualifiedContentEditorUrl(instance.NodeId);

                var flowTasks = instance.TaskInstances.OrderBy(t => t.ApprovalStep);

                var userIdToExclude = Utility.GetSettings().FlowType != (int)FlowType.All
                    ? instance.AuthorUserId
                    : int.MinValue;

                // always take get the emails for all previous users, sometimes they will be discarded later
                // easier to just grab em all, rather than doing so conditionally
                var emailsForAllTaskUsers = new List<string>();

                // in the loop, also store the last task to a variable, and keep the populated group
                var taskIndex = 0;
                var taskCount = flowTasks.Count();
                WorkflowTaskInstancePoco finalTask = null;
                foreach (var task in flowTasks)
                {
                    taskIndex += 1;

                    var group = Pr.PopulatedUserGroup(task.GroupId).First();
                    if (group != null)
                    {
                        emailsForAllTaskUsers.AddRange(group.PreferredEmailAddresses(userIdToExclude));

                        if (taskIndex == taskCount)
                        {
                            finalTask = task;
                            finalTask.UserGroup = group;
                        }
                    }
                }

                var to = new List<string>();
                var systemEmailAddress = Utility.GetSettings().Email;

                var body = "";

                switch (emailType)
                {
                    case EmailType.ApprovalRequest:
                        to = finalTask.UserGroup.PreferredEmailAddresses(userIdToExclude);
                        body = string.Format(EmailApprovalRequestString,
                            to.Count > 1 ? "Umbraco user" : finalTask.UserGroup.Name, docUrl, docTitle, instance.AuthorComment,
                            instance.AuthorUser.Name, instance.TypeDescription);

                        break;

                    case EmailType.ApprovalRejection:
                        to = emailsForAllTaskUsers;
                        to.Add(instance.AuthorUser.Email);
                        body = string.Format(EmailRejectedString,
                            "Umbraco user", docUrl, docTitle, finalTask.Comment,
                            finalTask.ActionedByUser.Name, instance.TypeDescription.ToLower());

                        break;

                    case EmailType.ApprovedAndCompleted:
                        to = emailsForAllTaskUsers;
                        to.Add(instance.AuthorUser.Email);

                        //Notify web admins
                        to.Add(systemEmailAddress);

                        if (instance.WorkflowType == WorkflowType.Publish)
                        {
                            var n = Utility.GetNode(instance.NodeId);
                            docUrl = UrlHelpers.GetFullyQualifiedSiteUrl(n.Url);
                        }

                        body = string.Format(EmailApprovedString,
                                   "Umbraco user", docUrl, docTitle,
                                   instance.TypeDescriptionPastTense.ToLower()) + "<br/>";

                        body += BuildProcessSummary(instance);

                        break;

                    case EmailType.ApprovedAndCompletedForScheduler:
                        to = emailsForAllTaskUsers;
                        to.Add(instance.AuthorUser.Email);

                        body = string.Format(EmailApprovedString,
                                   "Umbraco user", docUrl, docTitle,
                                   instance.TypeDescriptionPastTense.ToLower()) + "<br/>";

                        body += BuildProcessSummary(instance);

                        break;

                    case EmailType.WorkflowCancelled:
                        to = emailsForAllTaskUsers;

                        // include the initiator email
                        to.Add(instance.AuthorUser.Email);

                        body = string.Format(EmailCancelledString,
                            "Umbraco user", instance.TypeDescription, docUrl, docTitle, finalTask.ActionedByUser.Name, finalTask.Comment);
                        break;
                    case EmailType.SchedulerActionCancelled:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(emailType), emailType, null);
                }

                if (!to.Any()) return;

                var client = new SmtpClient();
                var msg = new MailMessage();

                if (!string.IsNullOrEmpty(systemEmailAddress))
                {
                    msg.From = new MailAddress(systemEmailAddress);
                }

                var subject = BuildEmailSubject(emailType, instance);

                msg.To.Add(string.Join(",", to.Distinct()));
                msg.Subject = subject;
                msg.Body = $"<!DOCTYPE HTML SYSTEM><html><head><title>{subject}</title></head><body><font face=\"verdana\" size=\"2\">{body}</font></body></html>";
                msg.IsBodyHtml = true;

                client.Send(msg);
            }
            catch (Exception e)
            {
                Log.Error("Error sending notifications", e);
            }
        }

        /// <summary>
        /// Builds workflow instance details markup.
        /// </summary>
        /// <returns>HTML tr inner html definition</returns>
        private static string BuildProcessSummary(WorkflowInstancePoco instance)
        {
            string result = $"{instance.TypeDescription} requested by {instance.AuthorUser.Name} on {instance.CreatedDate.ToString("dd/MM/yy")} - {instance.StatusName}<br/>";

            if (!string.IsNullOrEmpty(instance.AuthorComment))
            {
                result += $"&nbsp;&nbsp;Comment: <i>{instance.AuthorComment}</i>";
            }
            result += "<br/>";

            var index = 1;

            foreach (var taskInstance in instance.TaskInstances)
            {
                result += BuildTaskSummary(taskInstance, index) + "<br/>";
                index += 1;
            }

            return result + "<br/>";
        }

        /// <summary>
        /// Create simple html markup for an inactive workflow task.
        /// </summary>
        /// <param name="taskInstance">The task instance.</param>
        /// <param name="index"></param>
        /// <returns>HTML markup describing an active task instance.</returns>
        private static string BuildTaskSummary(WorkflowTaskInstancePoco taskInstance, int index)
        {
            var result = "";

            switch (taskInstance.Status)
            {
                case (int)TaskStatus.Approved:
                case (int)TaskStatus.Rejected:
                case (int)TaskStatus.Cancelled:

                    if (taskInstance.CompletedDate != null)
                    {
                        result += $"Stage {index}: {taskInstance.StatusName} by {taskInstance.ActionedByUser.Name} on {taskInstance.CompletedDate.Value.ToString("dd/MM/yy")}";
                    }

                    if (!string.IsNullOrEmpty(taskInstance.Comment))
                    {
                        result += $"<br/>&nbsp;&nbsp;Comment: <i>{taskInstance.Comment}</i>";
                    }

                    break;

                case (int)TaskStatus.NotRequired:

                    result += $"Stage {index}: Not required";

                    break;
            }

            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="emailType"></param>
        /// <param name="instance"></param>
        /// <returns></returns>
        private static string BuildEmailSubject(EmailType emailType, WorkflowInstancePoco instance)
        {
            return $"{WorkflowInstancePoco.EmailTypeName(emailType)} - {instance.Node.Name} ({instance.TypeDescription})";
        }
    }
}
