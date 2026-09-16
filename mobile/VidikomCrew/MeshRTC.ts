import { RTCPeerConnection, RTCIceCandidate, RTCSessionDescription, MediaStream } from 'react-native-webrtc';

export class MeshRTC {
  private ws: WebSocket | null = null;
  private localStream: MediaStream;
  private peers: Map<string, RTCPeerConnection> = new Map();
  private clientId: string = Math.random().toString(36).substring(7);

  constructor(
    private serverIp: string, 
    private roomPin: string, 
    stream: MediaStream, 
    private onRemoteStream: (peerId: string, stream: MediaStream) => void,
    private onPeerDisconnected: (peerId: string) => void
  ) {
    this.localStream = stream;
  }

  public connect() {
    this.ws = new WebSocket('ws://192.168.1.2:5160/ws?roomId=intercom&pin=');
    
    this.ws.onopen = () => {
      console.log('Connected to Director Signaling Server');
      this.sendSignal({ type: 'peer-hello', sender: this.clientId });
    };

    this.ws.onmessage = async (e) => {
      try {
        const msg = JSON.parse(e.data);
        if (msg.type !== 'webrtc-signal') return;
        
        const payload = msg.payload;
        if (payload.target && payload.target !== this.clientId) return; // Not for us

        const sender = payload.sender;

        if (payload.type === 'peer-hello') {
          // New peer joined, we should create an offer for them
          await this.createPeerAndOffer(sender);
        } else if (payload.type === 'offer') {
          await this.handleOffer(sender, payload.sdp);
        } else if (payload.type === 'answer') {
          await this.handleAnswer(sender, payload.sdp);
        } else if (payload.type === 'ice-candidate') {
          await this.handleIceCandidate(sender, payload.candidate);
        }
      } catch (err) {
        console.error(err);
      }
    };

    this.ws.onclose = () => {
      console.log('Disconnected from signaling server');
      this.disconnectAll();
    };
  }

  private sendSignal(payload: any) {
    if (this.ws?.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify({ type: 'webrtc-signal', payload }));
    }
  }

  private createPeerConnection(peerId: string): RTCPeerConnection {
    const pc = new RTCPeerConnection({
      iceServers: [{ urls: 'stun:stun.l.google.com:19302' }] // Usually not needed for local LAN, but good fallback
    });

    // Add our local mic
    this.localStream.getTracks().forEach(track => pc.addTrack(track, this.localStream));

    // Listen for their audio
    pc.ontrack = (event: any) => {
      if (event.streams && event.streams[0]) {
        this.onRemoteStream(peerId, event.streams[0]);
      }
    };

    pc.onicecandidate = (event: any) => {
      if (event.candidate) {
        this.sendSignal({
          type: 'ice-candidate',
          sender: this.clientId,
          target: peerId,
          candidate: event.candidate
        });
      }
    };

    pc.oniceconnectionstatechange = () => {
      if (pc.iceConnectionState === 'disconnected' || pc.iceConnectionState === 'failed') {
        this.onPeerDisconnected(peerId);
        pc.close();
        this.peers.delete(peerId);
      }
    };

    this.peers.set(peerId, pc);
    return pc;
  }

  private async createPeerAndOffer(peerId: string) {
    const pc = this.createPeerConnection(peerId);
    const offer = await pc.createOffer({});
    await pc.setLocalDescription(offer);
    this.sendSignal({
      type: 'offer',
      sender: this.clientId,
      target: peerId,
      sdp: offer
    });
  }

  private async handleOffer(peerId: string, sdp: any) {
    let pc = this.peers.get(peerId);
    if (!pc) pc = this.createPeerConnection(peerId);

    await pc.setRemoteDescription(new RTCSessionDescription(sdp));
    const answer = await pc.createAnswer();
    await pc.setLocalDescription(answer);

    this.sendSignal({
      type: 'answer',
      sender: this.clientId,
      target: peerId,
      sdp: answer
    });
  }

  private async handleAnswer(peerId: string, sdp: any) {
    const pc = this.peers.get(peerId);
    if (pc) {
      await pc.setRemoteDescription(new RTCSessionDescription(sdp));
    }
  }

  private async handleIceCandidate(peerId: string, candidate: any) {
    const pc = this.peers.get(peerId);
    if (pc) {
      await pc.addIceCandidate(new RTCIceCandidate(candidate));
    }
  }

  public disconnectAll() {
    this.peers.forEach(pc => pc.close());
    this.peers.clear();
    if (this.ws) {
      this.ws.close();
      this.ws = null;
    }
  }
}
