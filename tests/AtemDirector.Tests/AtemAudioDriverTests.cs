using System;
using System.Threading.Tasks;
using Xunit;
using Desktop.HardwareAbstraction;
using Core;
using System.Reflection;
using System.Collections.Concurrent;

namespace AtemDirector.Tests
{
    public class AtemAudioDriverTests
    {
        private object GetAudioInputState(SimAtem sim, long channel)
        {
            var field = typeof(SimAtem).GetField("_audioInputs", BindingFlags.NonPublic | BindingFlags.Instance);
            var arr = field.GetValue(sim) as System.Array; return arr.GetValue(channel - 1);
        }

        private double GetVolume(object audioInputState)
        {
            return (double)audioInputState.GetType().GetProperty("Volume").GetValue(audioInputState);
        }

        private bool GetOn(object audioInputState)
        {
            return (bool)audioInputState.GetType().GetProperty("On").GetValue(audioInputState);
        }

        [Fact]
        public async Task SetVolumeAsync_DelegatesToIAtemSwitch_WithCorrectPhysicalChannel()
        {
            var sim = new SimAtem();
            var driver = new AtemAudioDriver("test-atem", "Test", sim);
            
            await driver.SetVolumeAsync(3, -15.0);
            
            var state = GetAudioInputState(sim, 3);
            Assert.NotNull(state);
            Assert.Equal(-15.0, GetVolume(state));
        }

        [Fact]
        public async Task SetOnAsync_DelegatesToIAtemSwitch_WithCorrectPhysicalChannel()
        {
            var sim = new SimAtem();
            var driver = new AtemAudioDriver("test-atem", "Test", sim);
            
            await driver.SetOnAsync(3, true);
            
            var state = GetAudioInputState(sim, 3);
            Assert.NotNull(state);
            Assert.True(GetOn(state));
        }

        [Fact]
        public async Task HardwareStateChanged_PropagatesFromSimAtem()
        {
            var sim = new SimAtem();
            var driver = new AtemAudioDriver("test-atem", "Test", sim);
            
            bool fired = false;
            long physicalChannel = 0;
            driver.HardwareStateChanged += (devId, pChan, vol, on, afv) => 
            {
                if (devId == "test-atem")
                {
                    fired = true;
                    physicalChannel = pChan;
                }
            };
            
            await sim.SetAudioVolumeAsync(7, -5); 
            await sim.SetAudioOnAsync(7, true); // Triggers StateChanged
            
            Assert.True(fired);
            Assert.Equal(7, physicalChannel);
        }
    }
}


