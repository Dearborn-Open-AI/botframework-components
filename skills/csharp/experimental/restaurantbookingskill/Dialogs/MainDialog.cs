﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Luis;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Schema;
using Microsoft.Bot.Solutions;
using Microsoft.Bot.Solutions.Dialogs;
using Microsoft.Bot.Solutions.Responses;
using Microsoft.Bot.Solutions.Skills;
using Microsoft.Bot.Solutions.Skills.Models;
using Microsoft.Extensions.DependencyInjection;
using RestaurantBookingSkill.Models;
using RestaurantBookingSkill.Responses.Main;
using RestaurantBookingSkill.Responses.Shared;
using RestaurantBookingSkill.Services;
using RestaurantBookingSkill.Utilities;
using SkillServiceLibrary.Utilities;

namespace RestaurantBookingSkill.Dialogs
{
    public class MainDialog : ComponentDialog
    {
        private BotSettings _settings;
        private BotServices _services;
        private LocaleTemplateEngineManager _localeTemplateEngineManager;
        private UserState _userState;
        private ConversationState _conversationState;
        private IStatePropertyAccessor<RestaurantBookingState> _conversationStateAccessor;

        public MainDialog(
            IServiceProvider serviceProvider,
            IBotTelemetryClient telemetryClient)
            : base(nameof(MainDialog))
        {
            _settings = serviceProvider.GetService<BotSettings>();
            _services = serviceProvider.GetService<BotServices>();
            _localeTemplateEngineManager = serviceProvider.GetService<LocaleTemplateEngineManager>();
            TelemetryClient = telemetryClient;

            // Create conversation state properties
            var conversationState = serviceProvider.GetService<ConversationState>();
            _conversationStateAccessor = conversationState.CreateProperty<RestaurantBookingState>(nameof(BookingDialog));

            var steps = new WaterfallStep[]
            {
                IntroStepAsync,
                RouteStepAsync,
                FinalStepAsync,
            };

            AddDialog(new WaterfallDialog(nameof(MainDialog), steps));
            AddDialog(new TextPrompt(nameof(TextPrompt)));
            InitialDialogId = nameof(MainDialog);

            // RegisterDialogs
            AddDialog(serviceProvider.GetService<BookingDialog>() ?? throw new ArgumentNullException(nameof(BookingDialog)));
        }

        // Runs when the dialog is started.
        protected override async Task<DialogTurnResult> OnBeginDialogAsync(DialogContext innerDc, object options, CancellationToken cancellationToken = default)
        {
            if (innerDc.Context.Activity.Type == ActivityTypes.Message && !string.IsNullOrEmpty(innerDc.Context.Activity.Text))
            {
                // Get cognitive models for the current locale.
                var localizedServices = _services.GetCognitiveModels();

                // Run LUIS recognition on Skill model and store result in turn state.
                localizedServices.LuisServices.TryGetValue("Restaurant", out var skillLuisService);
                if (skillLuisService != null)
                {
                    var skillResult = await skillLuisService.RecognizeAsync<ReservationLuis>(innerDc.Context, cancellationToken);
                    innerDc.Context.TurnState.Add(StateProperties.ReservationLuis, skillResult);
                }
                else
                {
                    throw new Exception("The skill LUIS Model could not be found in your Bot Services configuration.");
                }

                // Run LUIS recognition on General model and store result in turn state.
                localizedServices.LuisServices.TryGetValue("General", out var generalLuisService);
                if (generalLuisService != null)
                {
                    var generalResult = await generalLuisService.RecognizeAsync<General>(innerDc.Context, cancellationToken);
                    innerDc.Context.TurnState.Add(StateProperties.GeneralLuisResult, generalResult);
                }
                else
                {
                    throw new Exception("The general LUIS Model could not be found in your Bot Services configuration.");
                }

                // Check for any interruptions
                var interrupted = await InterruptDialogAsync(innerDc, cancellationToken);

                if (interrupted)
                {
                    // If dialog was interrupted, return EndOfTurn
                    return EndOfTurn;
                }
            }

            return await base.OnBeginDialogAsync(innerDc, options, cancellationToken);
        }

        // Runs on every turn of the conversation.
        protected override async Task<DialogTurnResult> OnContinueDialogAsync(DialogContext innerDc, CancellationToken cancellationToken = default)
        {
            if (innerDc.Context.Activity.Type == ActivityTypes.Message && !string.IsNullOrEmpty(innerDc.Context.Activity.Text))
            {
                // Get cognitive models for the current locale.
                var localizedServices = _services.GetCognitiveModels();

                // Run LUIS recognition on Skill model and store result in turn state.
                localizedServices.LuisServices.TryGetValue("Restaurant", out var skillLuisService);
                if (skillLuisService != null)
                {
                    var skillResult = await skillLuisService.RecognizeAsync<ReservationLuis>(innerDc.Context, cancellationToken);
                    innerDc.Context.TurnState.Add(StateProperties.ReservationLuis, skillResult);
                }
                else
                {
                    throw new Exception("The skill LUIS Model could not be found in your Bot Services configuration.");
                }

                // Run LUIS recognition on General model and store result in turn state.
                localizedServices.LuisServices.TryGetValue("General", out var generalLuisService);
                if (generalLuisService != null)
                {
                    var generalResult = await generalLuisService.RecognizeAsync<General>(innerDc.Context, cancellationToken);
                    innerDc.Context.TurnState.Add(StateProperties.GeneralLuisResult, generalResult);
                }
                else
                {
                    throw new Exception("The general LUIS Model could not be found in your Bot Services configuration.");
                }

                // Check for any interruptions
                var interrupted = await InterruptDialogAsync(innerDc, cancellationToken);

                if (interrupted)
                {
                    // If dialog was interrupted, return EndOfTurn
                    return EndOfTurn;
                }
            }

            return await base.OnContinueDialogAsync(innerDc, cancellationToken);
        }

        // Runs on every turn of the conversation to check if the conversation should be interrupted.
        protected async Task<bool> InterruptDialogAsync(DialogContext innerDc, CancellationToken cancellationToken)
        {
            var interrupted = false;
            var activity = innerDc.Context.Activity;

            if (activity.Type == ActivityTypes.Message && !string.IsNullOrEmpty(activity.Text))
            {
                // Get connected LUIS result from turn state.
                var generalResult = innerDc.Context.TurnState.Get<General>(StateProperties.GeneralLuisResult);
                (var generalIntent, var generalScore) = generalResult.TopIntent();

                if (generalScore > 0.5)
                {
                    switch (generalIntent)
                    {
                        case General.Intent.Cancel:
                            {
                                var state = await _conversationStateAccessor.GetAsync(innerDc.Context, () => new RestaurantBookingState());
                                state.Clear();

                                await innerDc.Context.SendActivityAsync(_localeTemplateEngineManager.GetResponse(RestaurantBookingSharedResponses.CancellingMessage));
                                await innerDc.CancelAllDialogsAsync();
                                await innerDc.BeginDialogAsync(InitialDialogId);
                                interrupted = true;
                                break;
                            }

                        case General.Intent.Help:
                            {
                                await innerDc.Context.SendActivityAsync(_localeTemplateEngineManager.GetResponse(RestaurantBookingMainResponses.HelpMessage));
                                await innerDc.RepromptDialogAsync();
                                interrupted = true;
                                break;
                            }
                    }
                }
            }

            return interrupted;
        }

        // Handles introduction/continuation prompt logic.
        private async Task<DialogTurnResult> IntroStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (stepContext.Context.IsSkill())
            {
                // If the bot is in skill mode, skip directly to route and do not prompt
                return await stepContext.NextAsync();
            }
            else
            {
                // If bot is in local mode, prompt with intro or continuation message
                var promptOptions = new PromptOptions
                {
                    Prompt = stepContext.Options as Activity ?? _localeTemplateEngineManager.GetResponse(RestaurantBookingMainResponses.FirstPromptMessage)
                };

                if (stepContext.Context.Activity.Type == ActivityTypes.ConversationUpdate)
                {
                    promptOptions.Prompt = _localeTemplateEngineManager.GetResponse(RestaurantBookingMainResponses.WelcomeMessage);
                }

                return await stepContext.PromptAsync(nameof(TextPrompt), promptOptions, cancellationToken);
            }
        }

        // Handles routing to additional dialogs logic.
        private async Task<DialogTurnResult> RouteStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var activity = stepContext.Context.Activity;
            var state = await _conversationStateAccessor.GetAsync(stepContext.Context, () => new RestaurantBookingState());

            if (activity.Type == ActivityTypes.Message && !string.IsNullOrEmpty(activity.Text))
            {
                var result = stepContext.Context.TurnState.Get<ReservationLuis>(StateProperties.ReservationLuis);
                var intent = result?.TopIntent().intent;

                switch (intent)
                {
                    case ReservationLuis.Intent.Reservation:
                        {
                            return await stepContext.BeginDialogAsync(nameof(BookingDialog));
                        }

                    case ReservationLuis.Intent.None:
                        {
                            // No intent was identified, send confused message
                            await stepContext.Context.SendActivityAsync(_localeTemplateEngineManager.GetResponse(RestaurantBookingSharedResponses.DidntUnderstandMessage));
                            break;
                        }

                    default:
                        {
                            // intent was identified but not yet implemented
                            await stepContext.Context.SendActivityAsync(_localeTemplateEngineManager.GetResponse(RestaurantBookingSharedResponses.DidntUnderstandMessage));
                            break;
                        }
                }
            }

            // If activity was unhandled, flow should continue to next step
            return await stepContext.NextAsync();
        }

        // Handles conversation cleanup.
        private async Task<DialogTurnResult> FinalStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (stepContext.Context.IsSkill())
            {
                // EndOfConversation activity should be passed back to indicate that VA should resume control of the conversation
                var endOfConversation = new Activity(ActivityTypes.EndOfConversation)
                {
                    Code = EndOfConversationCodes.CompletedSuccessfully,
                    Value = stepContext.Result,
                };

                await stepContext.Context.SendActivityAsync(endOfConversation, cancellationToken);
                return await stepContext.EndDialogAsync();
            }
            else
            {
                return await stepContext.ReplaceDialogAsync(InitialDialogId, _localeTemplateEngineManager.GetResponse(RestaurantBookingMainResponses.CompletedMessage), cancellationToken);
            }
        }
    }
}