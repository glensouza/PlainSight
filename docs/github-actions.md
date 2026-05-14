# GitHub Actions Workflow Documentation

This document describes the CI/CD pipeline for PlainSight using GitHub Actions.

## Overview

The PlainSight project uses GitHub Actions for:
- Building Docker images for the server (Local to Runner)
- Building ARM64 binaries for Raspberry Pi players
- Managing local image retention (5 versions)
- Automated deployment to production server

## Workflow Files

- `.github/workflows/server.yml`: Handles server Docker builds and production deployment.
- `.github/workflows/player.yml`: Handles Raspberry Pi player ARM64 builds and release management.
- `.github/workflows/bump-minor.yml`: Automates version bumping on pull requests.

## Workflow Jobs

### 1. Server CI/CD (`server.yml`)

**Trigger**: 
- Push to `main` branch (specifically for server or shared code)
- Manual workflow dispatch

**Runner**: `self-hosted` (must be the production runner)

**Steps**:
1. **Checkout**: Pulls latest code into the runner's workspace.
2. **Set Version**: Reads `version.txt` and increments the patch number for the build.
3. **Build**: Builds a local Docker image.
4. **Deploy and Verify**:
   - Tags the existing `current` image as `previous` (for rollback).
   - Tags the new build as `current`.
   - Runs `docker compose up -d` using secrets injected into the environment.
   - Pings `/health` for verification.
   - **Auto-Rollback**: If health check fails, re-tags `previous` as `current` and restarts.
5. **Cleanup**: Retains only the 5 most recent version-tagged images locally.

**Required Secrets**:
- `POSTGRES_PASSWORD`: Database password.
- `CLOUDFLARE_TUNNEL_TOKEN`: Token for Cloudflare Tunnel.
- `PGADMIN_DEFAULT_EMAIL`: pgAdmin login.
- `PGADMIN_DEFAULT_PASSWORD`: pgAdmin password.
- `OBS_WEBSOCKET_URL` & `OBS_WEBSOCKET_PASSWORD`: For OBS integration.
- `ALERTS_EMAIL_PASSWORD`: For email alerts.

### 2. Player Release (`player.yml`)

**Trigger**: 
- Push to `main` (for player or shared code)
- Manual workflow dispatch

**Runner**: `self-hosted` (must have write access to the SMB share mount)

**Steps**:
1. **Checkout** & **Setup .NET 10**
2. **Build**: Compiles Player for ARM64 as a single-file binary.
3. **Copy to Share**: Moves the binary to the `/mnt/plainsight` (SMB) path.
4. **Sign Manifest**: 
   - Computes SHA-256 hash.
   - Creates a canonical JSON manifest.
   - Signs the manifest using `secrets.PLAINSIGHT_SIGNING_KEY` (ECDSA).
5. **Prune**: Keeps only the 3 newest player binaries on the share.

## Configuration

### Required Secrets

- `PLAINSIGHT_SIGNING_KEY`: ECDSA P-256 private key for player update verification.

### Required Variables

- `UPDATES_PATH`: Absolute path to the updates folder (e.g., `/mnt/plainsight/updates`).

## Architecture: Registry-Free

The system does **not** use an external container registry (like GHCR or Docker Hub). 
- Images are built and stored locally on the self-hosted runner.
- Deployment is performed by `docker compose` referencing these local images.
- This ensures the system remains functional even if external registry access is restricted.

## Monitoring & Troubleshooting

### View Status
- GitHub Repository → **Actions** tab.

### Deployment Fails
- Check runner logs: `docker logs plainsight-server`
- Verify SMB mount: `ls /mnt/plainsight`

### Rollback
Rollbacks are automated on health check failure. To manually rollback:
```bash
docker tag plainsight-server:previous plainsight-server:current
docker compose up -d plainsight-server
```
