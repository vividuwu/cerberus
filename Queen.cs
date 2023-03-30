using DSharpPlus;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using DSharpPlus.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Cerberus.Commands;
using Cerberus.Database;
using VRChat.API.Client;
using VRChat.API.Api;
using VRChat.API.Model;

namespace Cerberus {
    public class LoonieBot : ILoonieBot {
        private DiscordClient bot;
        private DatabaseMiddleware db;
        private ILogger logger;
        private Timer _timer;
        public LoonieBot(string token, DatabaseMiddleware db, VrchatLoginCredentials vrcLogin, ILogger<LoonieBot> _logger) {
            logger = _logger;

            bot = new DiscordClient(new DiscordConfiguration {
                Token = token,
                Intents = DiscordIntents.All
            });
            this.db = db;

            Configuration vrcConfig = new Configuration();
            vrcConfig.Username = vrcLogin.Username;
            vrcConfig.Password = vrcLogin.Password;
            vrcConfig.AddApiKey("apiKey", vrcLogin.ApiKey);
            vrcConfig.Timeout = 5000;

            AuthenticationApi auth = new AuthenticationApi(vrcConfig);
            CurrentUser user = auth.GetCurrentUser();
            // TwoFactorAuthCode authCode = new TwoFactorAuthCode();
            // Verify2FAResult res = auth.Verify2FA(authCode);

            SlashCommandsExtension commands = bot.UseSlashCommands(new SlashCommandsConfiguration {
                Services = new ServiceCollection().AddSingleton<DatabaseMiddleware>(db)
                .AddSingleton<Configuration>()
                .BuildServiceProvider()
            });

            commands.RegisterCommands<Vrchat>();
            commands.RegisterCommands<Util>();

            bot.Ready += OnReady;
            bot.GuildAvailable += OnGuildAvailable;
            bot.MessageReactionAdded += OnReaction;
            bot.MessageReactionRemoved += OnReactionRemoved;
        }
        public async Task StartAsync(CancellationToken token) {
            await bot.ConnectAsync();

            _timer = new Timer(UpdateStatusNumber, null, TimeSpan.Zero, TimeSpan.FromMinutes(10));
        }
        public async Task StopAsync(CancellationToken token) { 
            _timer.Change(Timeout.Infinite, 0);
            await bot.DisconnectAsync();
            bot.Dispose();
        }
        public void Dispose() {}

        public void UpdateStatusNumber(object state) {
            int onlinePlayers = VRChatUtils.OnlinePlayers().GetAwaiter().GetResult();

            bot.UpdateStatusAsync(new DiscordActivity(onlinePlayers + " degenerates", ActivityType.Watching), DSharpPlus.Entities.UserStatus.Online).GetAwaiter().GetResult();
        }

        private async Task OnReady(DiscordClient client, ReadyEventArgs eventArgs) {
            logger.LogInformation("Connected to {0}", client.CurrentUser.Username);

            int onlinePlayers = await VRChatUtils.OnlinePlayers();
            await bot.UpdateStatusAsync(new DiscordActivity(onlinePlayers + " degenerates", ActivityType.Watching), DSharpPlus.Entities.UserStatus.Online);
        }
        private async Task OnGuildAvailable(DiscordClient client, GuildCreateEventArgs eventArgs) {
            // await eventArgs.Guild.SystemChannel.SendMessageAsync("Sup bitches!");
        }
        private async Task OnReaction(DiscordClient client, MessageReactionAddEventArgs eventArgs) {
            DiscordMember member = await eventArgs.Guild.GetMemberAsync(eventArgs.User.Id);
            if (member.IsBot || member.IsCurrent) {
                return;
            }

            Reactionlistener listener;
            bool found = false;
            try {
                listener = await db.FetchReactionListenerAsync(eventArgs.Message.Id, eventArgs.Emoji);
                found = true;
            } catch (DatabseException) {
                return;
            }
            if (found) {
                await member.GrantRoleAsync(eventArgs.Guild.GetRole(listener.RoleId));
            }
        }
        private async Task OnReactionRemoved(DiscordClient client, MessageReactionRemoveEventArgs eventArgs) {
            DiscordMember member = await eventArgs.Guild.GetMemberAsync(eventArgs.User.Id);
            if (member.IsBot || member.IsCurrent) {
                return;
            }

            Reactionlistener listener;
            bool found = false;
            try {
                listener = await db.FetchReactionListenerAsync(eventArgs.Message.Id, eventArgs.Emoji);
                found = true;
            } catch (DatabseException) {
                return;
            }

            if (found) {
                await member.RevokeRoleAsync(eventArgs.Guild.GetRole(listener.RoleId));
            }
        }
    }

    public interface ILoonieBot : IHostedService, IDisposable {
        
    }
}