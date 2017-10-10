using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Data.Json;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace HoloSample
{
  class WebRTCWrapper
  {
    private Org.WebRtc.RTCPeerConnection rtcPeerConnection;
    private MessageWebSocket webSocket;

    public void Initialize(string signalServer)
    {
      Org.WebRtc.WebRTC.RequestAccessForMediaCapture().AsTask().ContinueWith(async antecedent =>
        {
          if (antecedent.Result)
          {
            Org.WebRtc.WebRTC.Initialize(CoreApplication.MainView.CoreWindow.Dispatcher);

            if (ConnectToSignalServer(signalServer))
              await ConnectToRTCPeer();
          }
        });
    }
    
    public void UnInitialize()
    {
      if (webSocket != null)
        webSocket.Close(1000, "Normal Shutdown");
    }

    private bool ConnectToSignalServer(string signalServer)
    {
      // Connect to Signal Server
      webSocket = new MessageWebSocket();
      webSocket.Control.MessageType = SocketMessageType.Utf8;

      webSocket.MessageReceived += WebSocket_MessageReceived;
      webSocket.ServerCustomValidationRequested += WebSocket_ServerCustomValidationRequested;
      webSocket.Closed += WebSocket_Closed;

      try
      {
        webSocket.ConnectAsync(new Uri(signalServer)).AsTask().Wait();

        return true;
      }
      catch (Exception)
      {
      }

      return false;
    }

    private async Task ConnectToRTCPeer()
    {
      var config = new Org.WebRtc.RTCConfiguration()
      {
        BundlePolicy = Org.WebRtc.RTCBundlePolicy.Balanced,
        IceServers = new List<Org.WebRtc.RTCIceServer>(),
        IceTransportPolicy = Org.WebRtc.RTCIceTransportPolicy.All
      };

      rtcPeerConnection = new Org.WebRtc.RTCPeerConnection(config);

      rtcPeerConnection.OnIceCandidate += RtcPeerConnection_OnIceCandidate;
      rtcPeerConnection.OnAddStream += RtcPeerConnection_OnAddStream;

      var mediaStreamConstraints = new Org.WebRtc.RTCMediaStreamConstraints()
      {
        audioEnabled = true,
        videoEnabled = true
      };

      Org.WebRtc.Media media = Org.WebRtc.Media.CreateMedia();
      Org.WebRtc.Media.SetDisplayOrientation(Windows.Graphics.Display.DisplayOrientations.Landscape);

      var videoCaptureDevices = media.GetVideoCaptureDevices();

      var videoDevice = videoCaptureDevices[0];

      var videoCaptueCapabilities = await videoDevice.GetVideoCaptureCapabilities();

      media.SelectVideoDevice(videoDevice);

      var chosenCapability = videoCaptueCapabilities[0];
      foreach (var capability in videoCaptueCapabilities)
      {
        if ((capability.Width < chosenCapability.Width && capability.Height < chosenCapability.Height) || (capability.Width == chosenCapability.Width && capability.Height == chosenCapability.Height && capability.FrameRate < chosenCapability.FrameRate))
          chosenCapability = capability;          
      }

      Org.WebRtc.WebRTC.SetPreferredVideoCaptureFormat((int)chosenCapability.Width, (int)chosenCapability.Height, (int)chosenCapability.FrameRate);

      Org.WebRtc.MediaStream localMediaStream = await media.GetUserMedia(mediaStreamConstraints);

      rtcPeerConnection.AddStream(localMediaStream);

      // Create Offer
      var offer = await rtcPeerConnection.CreateOffer();
      await rtcPeerConnection.SetLocalDescription(offer);

      // Send Offer to Signal Server
      await SendWebSocketMessage(webSocket, String.Format("{{\"type\":\"offer\",\"sdp\":\"{0}\"}}", EscapeJSon(offer.Sdp)));
    }

    private void RtcPeerConnection_OnAddStream(Org.WebRtc.MediaStreamEvent remoteMediaStream)
    {
      //throw new NotImplementedException();
    }

    private void RtcPeerConnection_OnIceCandidate(Org.WebRtc.RTCPeerConnectionIceEvent ice)
    {
      // Send ICE Candidate to Server
      SendWebSocketMessage(webSocket,
        String.Format("{{\"type\":\"ice\",\"candidate\":\"{0}\",\"sdpMid\":\"{1}\",\"sdpMLineIndex\":{2}}}",
        EscapeJSon(ice.Candidate.Candidate),
        ice.Candidate.SdpMid,
        ice.Candidate.SdpMLineIndex)).Wait();
    }

    private void WebSocket_Closed(IWebSocket sender, WebSocketClosedEventArgs args)
    {
      //throw new NotImplementedException();
    }

    private void WebSocket_ServerCustomValidationRequested(MessageWebSocket sender, WebSocketServerCustomValidationRequestedEventArgs args)
    {
      throw new NotImplementedException();
    }

    private void WebSocket_MessageReceived(MessageWebSocket sender, MessageWebSocketMessageReceivedEventArgs args)
    {
      try
      {
        DataReader reader = args.GetDataReader();
        reader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;

        // Read Message
        string message = reader.ReadString(reader.UnconsumedBufferLength);

        // Parse Message
        JsonObject json = JsonValue.Parse(message).GetObject();

        if (json.GetNamedString("type") == "answer")
          AnswerReceived(json.GetNamedString("sdp"));
        else
          if (json.GetNamedString("type") == "ice")
          RemoteICECandidateReceived(json.GetNamedString("candidate"), (ushort)json.GetNamedNumber("sdpMLineIndex"), json.GetNamedString("sdpMid"));
      }
      catch (Exception)
      {
      }
    }

    private async void AnswerReceived(string sdp)
    {
      // Set Answer
      Org.WebRtc.RTCSessionDescription answer = new Org.WebRtc.RTCSessionDescription()
      {
        Type = Org.WebRtc.RTCSdpType.Answer,
        Sdp = sdp
      };

      await rtcPeerConnection.SetRemoteDescription(answer);
    }

    private async void RemoteICECandidateReceived(string candidate, ushort sdpMLineIndex, string sdpMid)
    {
      // Add Remote ICE Candidate
      Org.WebRtc.RTCIceCandidate c = new Org.WebRtc.RTCIceCandidate()
      {
        Candidate = candidate,
        SdpMLineIndex = sdpMLineIndex,
        SdpMid = sdpMid
      };

      await rtcPeerConnection.AddIceCandidate(c);
    }

    private async Task SendWebSocketMessage(MessageWebSocket webSocket, String message)
    {
      DataWriter writer = new DataWriter(webSocket.OutputStream);
      writer.WriteString(message);
      await writer.StoreAsync();
    }

    private string EscapeJSon(string v)
    {
      v = v.Replace("\r", "\\r").Replace("\n", "\\n");
      return v;
    }
  }
}
