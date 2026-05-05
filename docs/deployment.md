# PlainSight Deployment Guide

This guide covers deploying the PlainSight server using Docker Compose on an ARM-based Mac, leveraging a self-hosted GitHub Actions runner and an external MyCloud SMB share.

## Architecture Overview

PlainSight uses a "Local-First" deployment model:
1. **Host (Mac)**: Acts as the primary server, build machine, and GitHub runner.
2. **Storage (MyCloud)**: External SMB share used for all persistent media and system files.
3. **Registry-Free**: Docker images are built locally on the host; no external image registry is used.

## Prerequisites

- ARM-based Mac (M1/M2/M3)
- Docker Desktop installed
- GitHub self-hosted runner configured on the Mac
- MyCloud SMB share mounted at `~/MyCloudHome` (via startup script)
- At least 4GB available RAM and 20GB disk space

## Installation & Configuration

### 1. Configure SMB Storage

Ensure your MyCloud share is mounted and contains a `PlainSight` folder at the root:
```bash
~/MyCloudHome/PlainSight
├── content/
├── updates/
└── screenshots/
```

### 2. Environment Variables

Create a `.env` file in the project root:

```bash
# PostgreSQL password (REQUIRED)
POSTGRES_PASSWORD=your_secure_password_here

# Cloudflare Tunnel Token (REQUIRED)
CLOUDFLARE_TUNNEL_TOKEN=your_token_here

# Admin Dashboard Initial Credentials
PGADMIN_DEFAULT_EMAIL=admin@example.com
PGADMIN_DEFAULT_PASSWORD=your_secure_password_here
```

### 3. GitHub Actions Runner Setup

The CI/CD pipeline (`server.yml`) requires two variables configured in your GitHub Repository settings (**Settings > Secrets and variables > Actions**):

- `DEPLOYMENT_PATH`: The absolute path to the directory where `docker-compose.yml` resides on your Mac.
- `UPDATES_PATH`: The absolute path to the `updates` folder on your mount (e.g., `/Users/admin/MyCloudHome/PlainSight/updates`).

### 4. Release Signing Keys

To enable player self-updates, you must provide an ECDSA P-256 keypair:

```bash
# Generate the keypair
openssl ecparam -name prime256v1 -genkey -noout -out signing.key
openssl pkcs8 -topk8 -nocrypt -in signing.key -out signing.pkcs8
openssl ec -in signing.key -pubout -out release-signing.pub
```

1. **Server**: Copy `release-signing.pub` to `src/PlainSight.Server/Keys/`.
2. **GitHub**: Add `signing.pkcs8` as a GitHub Secret named `PLAINSIGHT_SIGNING_KEY`.

## Deployment Workflow

### Automatic (CI/CD)

Whenever you push to the `main` branch, the self-hosted runner will:
1. Build a new Docker image tagged with the Git SHA.
2. Tag the current running image as `:previous`.
3. Tag the new image as `:current`.
4. Restart the `plainsight-server` container.
5. **Health Check**: Wait 2 minutes for a `200 OK` on the health endpoint.
6. **Auto-Rollback**: If the health check fails, the runner automatically re-tags the `:previous` image as `:current`, restarts the container, and fails the job.

### Manual Commands

```bash
# Start all services
docker compose up -d

# View deployment status
docker compose ps

# View logs
docker compose logs -f plainsight-server
```

## Maintenance & Operations

### Image Retention
The CI/CD pipeline automatically prunes old images, keeping only the **5 most recent** SHA-tagged images locally on the Mac to save disk space.

### Database Backups
```bash
docker compose exec postgres pg_dump -U plainsight plainsightdb > backup-$(date +%Y%m%d).sql
```

### Troubleshooting
If the server container fails to start, verify that the MyCloud share is correctly mounted at `~/MyCloudHome`. If the directory is missing, Docker may create a local folder, preventing the SMB mount from linking correctly.
