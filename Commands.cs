using CommandSystem;
using Exiled.API.Extensions;
using Exiled.API.Features;
using Exiled.CustomRoles.API;
using Exiled.CustomRoles.API.Features;
using PlayerRoles;
using MEC;

namespace SCPReplacer
{
    [CommandHandler(typeof(ClientCommandHandler))]
    public class Volunteer : ICommand
    {
        public string Command => "volunteer";

        public string[] Aliases => new[] { "v" };

        public string Description => "Volontaire pour devenir un SCP au début de partie\r\n";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (arguments.Count != 1)
            {
                response = Plugin.Singleton.Translation.WrongUsageMessage;
                return false;
            }

            if (Plugin.Singleton.HasReplacementCutoffPassed())
            {
                response = Plugin.Singleton.Translation.TooLateMessage;
                return false;
            }

            // Remove non-digits from input so they can type e.g. "SCP-079" and have it still work
            var requestedScp = arguments.FirstElement().ScpNumber();

            // Look in our list of SCPs awaiting replacement and see if any matches
            foreach (var role in Plugin.Singleton.ScpsAwaitingReplacement)
            {
                if (role.Name == requestedScp && Player.Get(sender) is Player player)
                {
                    if (player.IsScp && player.Role != RoleTypeId.Scp0492)
                    {
                        response = "Les SCP ne peuvent pas utiliser cette commande.";
                        return false;
                    }

                    if (role.Volunteers.Contains(player))
                    {
                        response = "Vous ne pouvez pas faire de bénévolat plus d'une fois à la fois.";
                        return false;
                    }

                    role.Volunteers.Add(player);

                    if (role.LotteryTimeout == null)
                    {
                        role.LotteryTimeout = Timing.CallDelayed(Plugin.Singleton.Config.LotteryPeriodSeconds, () =>
                        {
                            role.Replace();
                        });
                    }

                    response = $"Vous avez participé à la loterie pour devenir SCP {role.Name}.";
                    player.Broadcast(5,
                        Plugin.Singleton.Translation.BroadcastHeader +
                        Plugin.Singleton.Translation.EnteredLotteryBroadcast.Replace("%NUMBER%", requestedScp),
                        Broadcast.BroadcastFlags.Normal,
                        true
                    );
                    // replacement successful
                    return true;
                }
            }

            // SCP was not in our list of SCPs awaiting replacement
            if (Plugin.Singleton.ScpsAwaitingReplacement.IsEmpty())
            {
                response = Plugin.Singleton.Translation.NoEligibleSCPsError;
            }
            else
            {
                response = Plugin.Singleton.Translation.InvalidSCPError
                     + string.Join(", ", Plugin.Singleton.ScpsAwaitingReplacement); // display available SCP numbers
            }
            return false;
        }
    }


    [CommandHandler(typeof(ClientCommandHandler))]
    public class HumanCommand : ICommand
    {
        public string Command => "human";

        public string[] Aliases => new string[] { "no" };
        public string Description => "Renoncer à être un SCP et devenir une classe humaine aléatoire à la place (doit être utilisé vers le début du tour).";


        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (Player.Get(sender) is Player scpPlayer)
            {
                if (!scpPlayer.IsScp)
                {
                    response = "Vous devez être un SCP pour utiliser cette commande.";
                    return false;
                }
                if (scpPlayer.Role == RoleTypeId.Scp0492)
                {
                    response = "SCP 049-2 ne peut pas utiliser cette commande.";
                    return false;
                }
                var config = Plugin.Singleton.Config;
                var elapsedSeconds = Round.ElapsedTime.TotalSeconds;
                // Minimum required health (configurable percentage) of the SCP
                // when they quit to be eligible for replacement
                var requiredHealth = (int)(config.RequiredHealthPercentage / 100.0 * scpPlayer.MaxHealth);
                var customRole = scpPlayer.GetCustomRoles().FirstOrDefault();
                var scpNumber = customRole?.Name.ScpNumber() ?? scpPlayer.Role.ScpNumber();
                Log.Info($"{scpPlayer.Nickname} left {elapsedSeconds} seconds into the round, was SCP-{scpNumber} with {scpPlayer.Health}/{scpPlayer.MaxHealth} HP ({requiredHealth} required for replacement)");
                if (elapsedSeconds > config.QuitCutoff)
                {
                    response = "Cette commande doit être utilisée plus près du début du tour.";
                    return false;
                }
                if (scpPlayer.Health < requiredHealth)
                {
                    response = "Votre santé est trop faible pour utiliser cette commande.";
                    return false;
                }
                Plugin.Singleton.ScpLeft(scpPlayer);
                var newRole = UnityEngine.Random.value switch
                {
                    < 0.45f => RoleTypeId.ClassD,
                    < 0.9f => RoleTypeId.Scientist,
                    _ => RoleTypeId.FacilityGuard
                };
                response = $"Vous êtes devenu un {newRole}.";
                scpPlayer.Role.Set(newRole, Exiled.API.Enums.SpawnReason.LateJoin, RoleSpawnFlags.All);

                foreach (CustomRole custom in scpPlayer.GetCustomRoles())
                    custom.RemoveRole(scpPlayer);
                scpPlayer.Broadcast(10, Plugin.Singleton.Translation.BroadcastHeader + $"Vous êtes devenu un <color={newRole.GetColor().ToHex()}>{newRole.GetFullName()}</color>");
                return true;
            }
            else
            {
                response = "Vous devez être un joueur pour utiliser cette commande.";
                return false;
            }
        }
    }
}