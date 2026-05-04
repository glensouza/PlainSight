# PlainSight Deployment Guide

This guide covers deploying the PlainSight server using Docker Compose on macOS with Docker Desktop.

## Prerequisites

- macOS with Docker Desktop installed
- At least 2GB available RAM
- At least 10GB available disk space
- Network access for Docker image pulls

## Installation

### 1. Clone the Repository

```bash
git clone https://github.com/glensouza/PlainSight.git
cd PlainSight
```

### 2. Configure Environment Variables

**IMPORTANT:** Environment variables are now required for security.

Copy the example environment file:

```bash
cp .env.example .env
```

Edit `.env` and set secure passwords:

```bash
# PostgreSQL password (REQUIRED)
POSTGRES_PASSWORD=your_secure_password_here

# SMB credentials (REQUIRED)
SMB_USER=your_smb_username
SMB_PASSWORD=your_secure_smb_password
```

**Generate secure passwords:**

```bash
# On Linux/macOS
openssl rand -base64 32

# On Windows PowerShell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }))
```

**Security Notes:**
- Never commit `.env` files to version control (already in .gitignore)
- Use strong, randomly generated passwords (minimum 20 characters)
- Change default credentials immediately in production
- Store passwords securely (e.g., password manager)

### 3. Generate Release Signing Keys

To enable automatic player version updates, PlainSight uses ECDSA P-256 signatures to verify that binaries downloaded by the player are authentic. If the public key is not provided, the server will log a warning at startup and player updates will be disabled.

**Generate the keypair:**
```bash
# 1. Generate the private key
openssl ecparam -name prime256v1 -genkey -noout -out signing.key

# 2. Convert it to PKCS#8 format (for GitHub Actions)
openssl pkcs8 -topk8 -nocrypt -in signing.key -out signing.pkcs8

# 3. Extract the public key (for the Server)
openssl ec -in signing.key -pubout -out release-signing.pub
```

**Configure the keys:**
1. Copy the `release-signing.pub` file into `src/PlainSight.Server/Keys/release-signing.pub`. This file will be baked into your Docker image so the server can verify updates.
2. In your GitHub repository, go to **Settings > Secrets and variables > Actions**, and add a new secret named `PLAINSIGHT_SIGNING_KEY`. Paste the entire contents of `signing.pkcs8` into it.
3. Delete `signing.key` and `signing.pkcs8` from your local machine to keep the private key secure.

### 4. Start the Services

```bash
docker compose up -d
```

This command will:
- Pull the PostgreSQL 17 image
- Build the PlainSight.Server Docker image
- Pull the Samba file share image
- Create volumes for persistent data
- Start all services

### 4. Verify Deployment

Check that all containers are running:

```bash
docker compose ps
```

You should see three services running:
- `plainsight-postgres`
- `plainsight-server`
- `plainsight-samba`

### 5. Access the Application

Open your browser and navigate to:
- **Admin Interface**: http://localhost:8080
- **Health Check**: http://localhost:8080/health

## Service Configuration

### PostgreSQL Database

- **Port**: 5432
- **Database**: plainsightdb
- **Username**: plainsight
- **Password**: Set via `POSTGRES_PASSWORD` env var
- **Data Volume**: `postgres_data`

### PlainSight Server

- **Port**: 8080
- **Environment**: Production
- **File Share Mount**: `/mnt/signage`

### Samba File Share

- **Ports**: 139, 445
- **Username**: pi
- **Password**: secure
- **Share Name**: signage
- **Share Path**: `/share`

## Updating the Server

### Pull Latest Changes

```bash
git pull origin main
docker compose pull
docker compose up -d
```

### Manual Image Build

```bash
docker compose build plainsight-server
docker compose up -d plainsight-server
```

## Rollback

Docker Compose doesn't automatically keep old images, but you can:

### Tag Before Updating

```bash
docker tag plainsight-server:latest plainsight-server:backup-$(date +%Y%m%d)
```

### List Available Images

```bash
docker images | grep plainsight-server
```

### Rollback to Previous Version

```bash
docker compose down
docker tag plainsight-server:backup-20260125 plainsight-server:latest
docker compose up -d
```

## Monitoring

### View Logs

```bash
# All services
docker compose logs -f

# Specific service
docker compose logs -f plainsight-server
docker compose logs -f postgres
```

### Resource Usage

```bash
docker stats
```

## Maintenance

### Backup Database

```bash
docker compose exec postgres pg_dump -U plainsight plainsightdb > backup-$(date +%Y%m%d).sql
```

### Restore Database

```bash
cat backup-20260125.sql | docker compose exec -T postgres psql -U plainsight plainsightdb
```

### Clean Up Old Data

```bash
# Remove unused images
docker image prune -a

# Remove unused volumes (WARNING: This deletes data)
docker volume prune
```

## Troubleshooting

### Container Won't Start

```bash
# Check logs
docker compose logs plainsight-server

# Restart service
docker compose restart plainsight-server
```

### Database Connection Issues

```bash
# Check PostgreSQL is healthy
docker compose exec postgres pg_isready -U plainsight

# Reset database container
docker compose down
docker volume rm plainsight_postgres_data
docker compose up -d
```

### Port Conflicts

If ports 8080, 5432, 139, or 445 are already in use, modify `docker-compose.yml`:

```yaml
ports:
  - "8081:8080"  # Use different external port
```

## Security Considerations

1. **Change Default Passwords**: Update `POSTGRES_PASSWORD` in production
2. **Use HTTPS**: Configure reverse proxy (nginx, Traefik) for TLS
3. **Firewall Rules**: Restrict access to ports 5432, 139, 445
4. **Regular Updates**: Keep Docker images updated
5. **Backup Strategy**: Implement automated backups

## Performance Tuning

### PostgreSQL Configuration

Create `postgres.conf` and mount it:

```yaml
volumes:
  - ./postgres.conf:/etc/postgresql/postgresql.conf
```

### Resource Limits

Add to `docker-compose.yml`:

```yaml
services:
  plainsight-server:
    deploy:
      resources:
        limits:
          cpus: '2'
          memory: 2G
```

## Production Checklist

- [ ] Change default PostgreSQL password
- [ ] Configure automated backups
- [ ] Set up monitoring and alerts
- [ ] Configure HTTPS/TLS
- [ ] Review security settings
- [ ] Test rollback procedure
- [ ] Document custom configurations
- [ ] Set up log aggregation
