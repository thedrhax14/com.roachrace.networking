using System;
using System.Collections.Generic;
using Dissonance;
using RoachRace.UI.Models;
using UnityEngine;

namespace RoachRace.Networking
{
    public class DissonancePlayerSpeakingObserver : MonoBehaviour
    {
        [SerializeField] private DissonanceComms dissonanceComms;
        [SerializeField] private GamePlayersModel gamePlayersModel;

        Dictionary<string, VoicePlayer> _voicePlayers = new();

        struct VoicePlayer
        {
            DissonancePlayerSpeakingObserver dissonancePlayerSpeakingObserver;
            public VoicePlayerState State;

            public VoicePlayer SetObserver(DissonancePlayerSpeakingObserver observer)
            {
                dissonancePlayerSpeakingObserver = observer;
                return this;
            }

            public VoicePlayer SetState(VoicePlayerState state)
            {
                State = state;
                State.OnStartedSpeaking += OnStartedSpeaking;
                State.OnStoppedSpeaking += OnStoppedSpeaking;
                return this;
            }

            void OnStartedSpeaking(VoicePlayerState state)
            {
                dissonancePlayerSpeakingObserver.gamePlayersModel.SetPlayerAmplitude(state.Name, state.Amplitude);
                dissonancePlayerSpeakingObserver.gamePlayersModel.SetPlayerSpeaking(state.Name, true);
            }

            void OnStoppedSpeaking(VoicePlayerState state)
            {
                dissonancePlayerSpeakingObserver.gamePlayersModel.SetPlayerAmplitude(state.Name, 0f);
                dissonancePlayerSpeakingObserver.gamePlayersModel.SetPlayerSpeaking(state.Name, false);
            }
        }

        void Awake()
        {
            dissonanceComms.OnPlayerEnteredRoom += OnPlayerEnteredRoom;
            dissonanceComms.OnPlayerExitedRoom += OnPlayerExitedRoom;
        }

        void OnPlayerEnteredRoom(VoicePlayerState state, string roomName)
        {
            Debug.Log($"[{nameof(DissonancePlayerSpeakingObserver)}] Player '{state.Name}' entered room '{roomName}'.");
            if(_voicePlayers.ContainsKey(state.Name)) return;
            _voicePlayers.Add(state.Name, new VoicePlayer());
            _voicePlayers[state.Name].SetObserver(this).SetState(state);
        }

        void OnPlayerExitedRoom(VoicePlayerState state, string roomName)
        {
            Debug.Log($"[{nameof(DissonancePlayerSpeakingObserver)}] Player '{state.Name}' exited room '{roomName}'.");
            if(!_voicePlayers.ContainsKey(state.Name)) return;
            _voicePlayers.Remove(state.Name);
        }
    }
}