using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace Punchy.Net
{
    /// <summary>
    /// Online play through Unity Relay, so friends on other networks can join without port forwarding: the host asks
    /// Relay for an allocation and gets a short join code to share; joiners trade the code for their own way in.
    /// Signs in anonymously to Unity Services first (the project's Unity Cloud link provides the project id).
    /// </summary>
    public static class RelaySessions
    {
        /// <summary>The current game's join code while hosting online (null otherwise).</summary>
        public static string JoinCode { get; private set; }
        /// <summary>What's happening, or what went wrong, for the F2 panel.</summary>
        public static string Status { get; private set; }
        public static bool Busy { get; private set; }

        public const int MaxPlayers = 8;

        static async Task SignIn()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        /// <summary>Starts a new game hosted online; on success JoinCode is the code to share.</summary>
        public static async void Host()
        {
            if (Busy) return;
            Busy = true;
            Status = "Getting an online code…";
            try
            {
                await SignIn();
                var allocation = await RelayService.Instance.CreateAllocationAsync(MaxPlayers - 1);
                var code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                GameBootstrap.HostRelay(allocation.ToRelayServerData("dtls"));
                JoinCode = code;
                Status = null;
                Debug.Log($"RelaySessions: hosting online, join code {code}");
            }
            catch (Exception e)
            {
                Status = Explain(e);
                Debug.LogWarning($"RelaySessions: couldn't host online: {e}");
            }
            Busy = false;
        }

        /// <summary>Joins an online game by its code.</summary>
        public static async void Join(string code)
        {
            if (Busy || string.IsNullOrWhiteSpace(code)) return;
            Busy = true;
            Status = "Joining…";
            try
            {
                await SignIn();
                var allocation = await RelayService.Instance.JoinAllocationAsync(code.Trim().ToUpperInvariant());
                GameBootstrap.JoinRelay(allocation.ToRelayServerData("dtls"));
                JoinCode = null;
                Status = null;
            }
            catch (Exception e)
            {
                Status = Explain(e);
                Debug.LogWarning($"RelaySessions: couldn't join {code}: {e}");
            }
            Busy = false;
        }

        /// <summary>Hosting or joining any other way ends the online game's code.</summary>
        public static void Forget() => JoinCode = null;

        static string Explain(Exception e)
        {
            var message = e.Message;
            if (e is RelayServiceException relay)
            {
                if (relay.Reason == RelayExceptionReason.JoinCodeNotFound || relay.Reason == RelayExceptionReason.InvalidRequest)
                    return "No game with that code (check it, or it may have ended).";
                if (relay.Reason == RelayExceptionReason.Forbidden || relay.Reason == RelayExceptionReason.Unauthorized)
                    return "Relay isn't enabled for this project (enable it in the Unity Cloud dashboard).";
            }
            if (e is AuthenticationException || e is ServicesInitializationException)
                return "Couldn't sign in to Unity Services (offline?).";
            return message.Length > 120 ? message.Substring(0, 120) + "…" : message;
        }
    }
}
