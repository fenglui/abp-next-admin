﻿using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.TextTemplating;

namespace LINGYUN.Abp.Notifications;

[Authorize]
public class NotificationAppService : ApplicationService, INotificationAppService
{
    protected ITemplateContentProvider TemplateContentProvider { get; }
    protected INotificationSender NotificationSender { get; }
    protected INotificationDefinitionManager NotificationDefinitionManager { get; }

    public NotificationAppService(
        INotificationSender notificationSender,
        ITemplateContentProvider templateContentProvider,
        INotificationDefinitionManager notificationDefinitionManager)
    {
        NotificationSender = notificationSender;
        TemplateContentProvider = templateContentProvider;
        NotificationDefinitionManager = notificationDefinitionManager;
    }

    public async virtual Task<ListResultDto<NotificationGroupDto>> GetAssignableNotifiersAsync()
    {
        var groups = new List<NotificationGroupDto>();
        var defineGroups = await NotificationDefinitionManager.GetGroupsAsync();

        foreach (var group in defineGroups)
        {
            if (!group.AllowSubscriptionToClients)
            {
                continue;

            }
            var notificationGroup = new NotificationGroupDto
            {
                Name = group.Name,
                DisplayName = group.DisplayName.Localize(StringLocalizerFactory)
            };

            foreach (var notification in group.Notifications)
            {
                if (!notification.AllowSubscriptionToClients)
                {
                    continue;
                }

                var notificationChildren = new NotificationDto
                {
                    Name = notification.Name,
                    DisplayName = notification.DisplayName.Localize(StringLocalizerFactory),
                    Description = notification.Description?.Localize(StringLocalizerFactory) ?? notification.Name,
                    Lifetime = notification.NotificationLifetime,
                    Type = notification.NotificationType,
                    ContentType = notification.ContentType
                };

                notificationGroup.Notifications.Add(notificationChildren);
            }

            groups.Add(notificationGroup);
        }

        return new ListResultDto<NotificationGroupDto>(groups);
    }

    public async virtual Task<ListResultDto<NotificationTemplateDto>> GetAssignableTemplatesAsync()
    {
        var templates = new List<NotificationTemplateDto>();
        var notifications = (await NotificationDefinitionManager
            .GetNotificationsAsync())
            .Where(n => n.Template != null);

        foreach (var notification in notifications)
        {
            templates.Add(
                new NotificationTemplateDto
                {
                    Name = notification.Name,
                    Culture = CultureInfo.CurrentCulture.Name,
                    Title = notification.DisplayName.Localize(StringLocalizerFactory),
                    Description = notification.Description?.Localize(StringLocalizerFactory),
                });
        }

        return new ListResultDto<NotificationTemplateDto>(templates);
    }

    public async virtual Task SendAsync(NotificationSendDto input)
    {
        var notification = await GetNotificationDefinition(input.Name);

        UserIdentifier user = null;
        if (input.ToUserId.HasValue)
        {
            user = new UserIdentifier(input.ToUserId.Value, input.ToUserName);
        }

        if (!input.TemplateName.IsNullOrWhiteSpace())
        {
            if (notification.Template == null)
            {
                throw new BusinessException(
                    NotificationsErrorCodes.NotificationTemplateNotFound,
                    $"The notification template {input.TemplateName} does not exist!")
                    .WithData("Name", input.TemplateName);
            }
            var notificationTemplate = new NotificationTemplate(
                notification.Name,
                culture: input.Culture ?? CultureInfo.CurrentCulture.Name,
                formUser: CurrentUser.Name ?? CurrentUser.UserName,
                data: input.Data);

            await NotificationSender
                .SendNofiterAsync(
                    name: input.Name,
                    template: notificationTemplate,
                    user: user,
                    tenantId: CurrentTenant.Id,
                    severity: input.Severity);
        }
        else
        {
            var notificationData = new NotificationData();
            notificationData.ExtraProperties.AddIfNotContains(input.Data);

            notificationData = NotificationData.ToStandardData(notificationData);

            await NotificationSender
                .SendNofiterAsync(
                    name: input.Name,
                    data: notificationData,
                    user: user,
                    tenantId: CurrentTenant.Id,
                    severity: input.Severity);
        }
    }

    protected async virtual Task<NotificationDefinition> GetNotificationDefinition(string name)
    {
        return await NotificationDefinitionManager.GetAsync(name);
    }
}
