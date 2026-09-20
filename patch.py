import re

with open('desktop/AtemAudioMonitor.cs', 'r') as f:
    content = f.read()

injection = '''
                                    try {
                                        Guid eqIid = typeof(IBMDSwitcherFairlightAudioEqualizer).GUID;
                                        source.GetEffect(ref eqIid, out IntPtr eqPtr);
                                        if (eqPtr != IntPtr.Zero) {
                                            var eq = (IBMDSwitcherFairlightAudioEqualizer)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(eqPtr);
                                            var eqCb = new FairlightEqCb(inputId, _eqUpdateCallback, eq);
                                            eq.AddCallback(eqCb);
                                            _callbacks.Add(new AttachedCallback(eq, eqCb));
                                            
                                            eq.CreateIterator(typeof(IBMDSwitcherFairlightAudioEqualizerBandIterator).GUID, out IntPtr iterPtr);
                                            if (iterPtr != IntPtr.Zero) {
                                                var iter = (IBMDSwitcherFairlightAudioEqualizerBandIterator)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(iterPtr);
                                                for (int bIdx = 0; bIdx < 6; bIdx++) {
                                                    iter.Next(out IBMDSwitcherFairlightAudioEqualizerBand band);
                                                    if (band == null) break;
                                                    var bandCb = new FairlightEqBandCb(inputId, bIdx, _eqUpdateCallback, band);
                                                    band.AddCallback(bandCb);
                                                    _callbacks.Add(new AttachedCallback(band, bandCb));
                                                }
                                            }
                                        }
                                    } catch { }
'''

content = content.replace('_callbacks.Add(new AttachedCallback(source, cb));', '_callbacks.Add(new AttachedCallback(source, cb));\n' + injection)

with open('desktop/AtemAudioMonitor.cs', 'w') as f:
    f.write(content)
