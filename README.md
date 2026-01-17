# RoachRace.Networking Package

## Overview
This package contains FishNet networking integration code for RoachRace multiplayer racing game. It bridges the Edgegap deployment system with FishNet's Tugboat transport to enable automatic connection to deployed game servers.

## Components

### NetworkConnectionObserver
MonoBehaviour that implements the Observer pattern to automatically connect FishNet clients when server connection information becomes available.

**Features:**
- Observes `ServersModel.ConnectionInfo` Observable for server IP/Port changes
- Automatically validates connection info before connecting
- Updates FishNet Tugboat transport with server address and port
- Supports auto-connect mode (configurable via Inspector)
- Handles disconnection and reconnection scenarios
- Provides manual connect/disconnect methods for custom workflows

**Usage:**
1. Add `NetworkConnectionObserver` component to a GameObject in your scene
2. Assign references:
   - `ServersModel` - The ScriptableObject model containing server data
   - `Tugboat` - The FishNet transport component (auto-finds if not assigned)
3. Configure `autoConnect` toggle (default: true)

**Flow:**
```
DeploymentService → ServersModel.SelectServer(deployment) 
                 → ServersModel.ConnectionInfo.Value updated
                 → NetworkConnectionObserver.OnNotify(connectionInfo)
                 → tugboat.SetClientAddress(ip) + tugboat.SetPort(port)
                 → tugboat.StartConnection(false) [if autoConnect=true]
```

## Architecture

### Dependencies
- **RoachRace.Data** - Pure C# data models (`ServerConnectionInfo`)
- **RoachRace.UI** - UI framework with Observer pattern and models
- **FishNet.Runtime** - FishNet networking framework (Tugboat transport)

### Observer Pattern Integration
The component implements `IObserver<ServerConnectionInfo>` from the UI framework's observer pattern system. It attaches/detaches from the Observable in `OnEnable`/`OnDisable` lifecycle methods to prevent memory leaks.

### FishNet Integration
Uses FishNet's `Tugboat` transport API:
- `SetClientAddress(string)` - Set server IP address
- `SetPort(ushort)` - Set server port
- `StartConnection(false)` - Start client connection (false = client mode)
- `StopConnection(false)` - Stop client connection
- `GetConnectionState(false)` - Check current client connection state

## Assembly Definition
**Name:** `RoachRace.Networking`
**References:**
- RoachRace.Data
- RoachRace.UI
- FishNet.Runtime (GUID: 9e24947de15b9834991c9d8411ea37cf)
- FishNet.Codegen.Cecil (GUID: 84651a3751eca9349aac36a85bdc7d73)
- FishNet.Demos (GUID: f51ebe6a0ceec4240a699833d6309b23)

## Data Flow Example

1. **User creates server via PlayWindow:**
   - `PlayWindow.OnCreateRoomClicked()` → `DeploymentService.CreateDeployment(serverName)`

2. **Deployment service deploys and monitors:**
   - POST to Edgegap API (`v2/deployments`)
   - Polls status endpoint (`v1/status/{request_id}`)
   - Extracts IP and UDP port from deployment status

3. **Service updates model:**
   - `DeploymentService.OnDeploymentReady()` creates `ServerDeployment`
   - Calls `serversModel.SelectServer(deployment)`

4. **Model notifies observers:**
   - `ServersModel.SelectServer()` updates `ConnectionInfo.Value`
   - Observable notifies all attached observers

5. **Network observer connects:**
   - `NetworkConnectionObserver.OnNotify()` receives `ServerConnectionInfo`
   - Validates IP/port
   - Updates Tugboat transport
   - Auto-connects if enabled

6. **FishNet connects:**
   - Tugboat initiates UDP connection to server
   - FishNet handles handshake and session establishment
   - Game is ready for networked gameplay

## Best Practices

### Auto-Connect
Keep `autoConnect` enabled for streamlined user experience. Disable only if you need custom connection logic or user confirmation before connecting.

### Error Handling
The component logs all connection attempts and errors. Monitor Unity Console for:
- `[NetworkConnectionObserver] Valid connection info received: {ip}:{port}`
- `[NetworkConnectionObserver] Connecting to server...`
- `[NetworkConnectionObserver] Client connection started successfully`

### Multiple Connections
The component automatically stops existing client connections before starting new ones to prevent connection conflicts.

### Scene Setup
Place `NetworkConnectionObserver` on a persistent GameObject or in your main menu scene to ensure it's active when server deployments complete.

## Debugging

### Connection Not Starting
- Check Tugboat reference is assigned
- Verify `autoConnect` is enabled (or call `ConnectToServer()` manually)
- Ensure `ServersModel.ConnectionInfo` is being updated
- Check Console for validation errors

### Invalid Connection Info
The component validates:
- IP is not null/empty
- Port is between 1-65535

If validation fails, check `DeploymentService.OnDeploymentReady()` is correctly extracting IP/port from API response.

### Observer Not Triggering
- Verify component is enabled (`OnEnable` attaches observer)
- Check `ServersModel` reference is assigned
- Ensure Observable's `.Value` property is being set (not just modifying the object)

## Future Enhancements
- Connection timeout handling
- Retry logic for failed connections
- Connection state visualization
- Server browser integration
- Ping/latency display before connecting
