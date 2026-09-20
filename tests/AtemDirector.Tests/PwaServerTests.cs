using System;
using System.Text.Json;
using Xunit;
using Desktop;
using System.Threading.Tasks;

namespace AtemDirector.Tests
{
    public class PwaServerTests
    {
        [Fact]
        public void HandleRelayMessage_AudioControl_ParsesAndFiresEvent()
        {
            var pwa = PwaServer.Instance;
            bool fired = false;
            long testInputId = 0;
            double? testVol = null;
            bool? testOn = null;
            bool? testAfv = null;
            
            pwa.OnAudioControl = (id, vol, on, afv) => 
            {
                fired = true;
                testInputId = id;
                testVol = vol;
                testOn = on;
                testAfv = afv;
            };

            // Needs an active room with matching pin/code to be processed, or does it?
            // Let's create a test JSON.
            // Oh, we might need RoomManager.ActiveRoom to not be null if we check it in the test.
            RoomManager.CreateRoom("TEST1", "Dir", Desktop.NetworkMode.LAN, "wss://", "TEST1", "1234");

            var json = JsonSerializer.Serialize(new {
                type = "audio-control",
                code = "1234",
                input = 42,
                volume = -15.5,
                on = true,
                afv = false
            });

            pwa.HandleRelayMessage(json);
            
            Assert.True(fired);
            Assert.Equal(42, testInputId);
            Assert.Equal(-15.5, testVol);
            Assert.True(testOn);
            Assert.False(testAfv);
        }
    }
}
