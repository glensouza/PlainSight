# Network & Cloudflare Management Guide

This document provides instructions for managing the PlainSight infrastructure, specifically focusing on the integration between the **UniFi Network** (on-site) and **Cloudflare Zero Trust** (cloud).

## 🏗️ Physical Network (UniFi)

The system is deployed on a dedicated **Tech VLAN** to isolate signage traffic from congregational and staff networks.

### 1. VLAN Configuration
- **Network Name:** Tech VLAN
- **Purpose:** All Raspberry Pi Players and the PlainSight Server.
- **Isolation:** "Device Isolation" (Guest Policy) should be **OFF** for this VLAN to allow the Server to communicate with the database (if local), but **Inter-VLAN routing** should be restricted at the Firewall level.

### 2. SSID Configuration
- **Name:** Tech-Signage (or equivalent)
- **Security:** WPA2/WPA3-Personal with a strong, rotating passkey.
- **Band:** 5GHz preferred for high-bitrate video streaming; 2.4GHz for better range if needed.

### 3. Firewall Rules (UniFi Gateway)
To maintain a "Zero Trust" posture, the following rules are recommended on the UniFi Security Gateway/Dream Machine:
- **Allow:** Outbound traffic on ports `443/tcp` and `53/udp` (HTTPS and DNS).
- **Block:** All *inbound* traffic from the internet (Cloudflare Tunnel handles this).
- **Block:** Access from the Tech VLAN to the "Default" or "Management" VLANs.

---

## ☁️ Cloud Edge (Cloudflare)

We use Cloudflare to provide a secure public entry point without exposing the church's public IP address.

### 1. Cloudflare Tunnel (The "Secret Sauce")
Instead of using Port Forwarding on the UniFi router, we use `cloudflared`.

- **Mechanism:** The Server runs a `cloudflared` daemon that creates an outbound-only connection to Cloudflare.
- **Docker Integration:** We run `cloudflared` as a container in our `docker-compose.yml`.
- **Benefit:** You don't need a Static IP from your ISP. Even if the church's IP changes, the tunnel stays up.
- **Management:** Managed via the [Cloudflare Zero Trust Dashboard](https://one.dash.cloudflare.com/).
- **Configuration:** You only need to provide the `CLOUDFLARE_TUNNEL_TOKEN` in your `.env` file. In the Cloudflare dashboard, point your subdomains to `http://plainsight-server:8080` (the internal Docker network address).

### 2. DNS & Subdomains
| Subdomain | Target | Access Policy |
| :--- | :--- | :--- |
| `plainsight.coronasda.church` | PlainSight Dashboard (Port 8080) | **Cloudflare Access** (Email/SSO) |
| `api.plainsight.coronasda.church` | PlainSight API (Port 8080) | **Bypass** (Protected by `X-Api-Key`) |
| `db.plainsight.coronasda.church` | pgAdmin Interface (Port 80) | **Cloudflare Access** (Strict) |

### 3. Cloudflare Access (Zero Trust)
To protect the `admin` and `db` dashboards:
1. Navigate to **Access > Applications**.
2. Add both `plainsight.coronasda.church` and `db.plainsight.coronasda.church`.
3. Create a policy allowing only specific email addresses or a specific domain (e.g., `@coronasda.church`).
4. This adds a login screen *before* the user even reaches your server.

---

## 🛠️ Maintenance & Troubleshooting

### If a Pi cannot connect:
1. **Check UniFi:** Is the device visible in the UniFi Client list? Is it on the correct Tech VLAN?
2. **Check DNS:** From the Pi terminal, run `curl -I https://api.yourdomain.com/api/device/heartbeat`. It should return a `405 Method Not Allowed` (which is good, it means it reached the API).
3. **Check Signal:** If using Wi-Fi, ensure the signal strength is above -70dBm in the UniFi dashboard.

### If the Dashboard is down:
1. **Check Tunnel:** In Cloudflare Zero Trust, is the Tunnel status "Healthy"?
2. **Check Server:** Is the `plainsight.service` running on the host?
   ```bash
   sudo systemctl status plainsight
   ```

---

## 💾 Common Storage (WD My Cloud Home)

The **WD My Cloud Home** serves as the central repository for rendered video content. Unlike standard NAS devices, the "Home" model requires explicit activation for local network access.

### ⚠️ Security Note: Subdomains
**Do NOT** create a subdomain or Cloudflare Tunnel for the WD My Cloud Home. SMB access is for local network use only. Performance for high-bitrate video relies on your local 1Gbps / 5GHz UniFi connection.

### 1. Enable Local Network Access (Mandatory)
Standard WD account credentials will **not** work for SMB mounting.
1. Open the **My Cloud Home** mobile app or log in at [home.mycloud.com](https://home.mycloud.com).
2. Go to **Settings** > **[Device Name]** > **Local Network Access**.
3. Toggle **Local Network Access** to **ON**.
4. Create a **Local Username** and **Local Password**.
5. Note the **IP Address** (you should also reserve this IP in your UniFi dashboard).

### 2. Mounting Instructions (Linux / Raspberry Pi)
The share name on a My Cloud Home is usually your **Local Username**.

#### Step A: Create Credentials File
Create `/etc/plainsight/nas-credentials`:
```text
username=your_local_username
password=your_local_password
```
`sudo chmod 600 /etc/plainsight/nas-credentials`

#### Step B: Update /etc/fstab
Add the following line (My Cloud Home requires `vers=3.0`):
```bash
# Mount WD My Cloud Home share
//<NAS_IP>/<LOCAL_USERNAME> /mnt/plainsight cifs credentials=/etc/plainsight/nas-credentials,iocharset=utf8,rw,file_mode=0640,dir_mode=0750,uid=plainsight,gid=plainsight,vers=3.0,nofail 0 0
```

#### Step C: Mount and Verify
```bash
sudo mkdir -p /mnt/plainsight
sudo mount -a
ls /mnt/plainsight
```

### 3. Mounting Instructions (Windows Server)
1. In File Explorer, select **Map network drive**.
2. Folder: `\\<NAS_IP>\<LOCAL_USERNAME>`.
3. Select **Connect using different credentials** and use the **Local Username/Password** created in Step 1.

## 🔒 Security Summary
- **No Open Ports:** No inbound ports are open on the UniFi firewall.
- **Encrypted Everywhere:** Traffic is HTTPS from the Pi to Cloudflare, and TLS through the Tunnel to the Server.
- **Identity-Based:** Access to the admin panel is restricted by identity (Cloudflare Access), not just a password.
