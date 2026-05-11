# NDI Discovery Service Setup

When running PlainSight Server in a Docker container (especially on macOS), the default NDI mDNS discovery often fails because Docker bridge networks block multicast traffic. 

The most reliable solution is to run the **NDI Discovery Service** on your host Mac and point the container to it.

## 1. Install NDI Tools on your Mac
1. Download and install [NDI Tools for Mac](https://ndi.video/tools/).
2. You specifically need the **NDI Discovery Service** (part of the NDI SDK or advanced tools).

## 2. Start the Discovery Service
On your Mac (the host running Docker), open a terminal and run:

```bash
# This binary is usually located in /usr/local/bin or within the NDI SDK
ndi-discovery-service
```

Alternatively, you can find the "NDI Access Manager" app in your Applications, go to the **External Sources** or **Advanced** tab, and ensure the Discovery Service is enabled.

## 3. Configure PlainSight Server
You need to tell the PlainSight Docker container where the Discovery Service is running.

1. Get the local IP address of your Mac (e.g., `192.168.1.50`).
2. Add the `NDI_DISCOVERY_SERVER` environment variable to your GitHub Secrets or `.env` file.

### GitHub Secret Method (Recommended)
Add a secret named `NDI_DISCOVERY_SERVER` with the value of your Mac's IP.

### Docker Compose Update
The server looks for the `NDI_CONFIG_DIR` or specific NDI variables. We will update the `docker-compose.yml` to pass this through.

## 4. Why this works
Instead of broadcasting "Where is the NDI source?" to the whole network (which Docker blocks), the container now sends a direct request to your Mac: "Hey 192.168.1.50, give me the list of NDI sources." 

This is 100% reliable even in isolated container networks.
