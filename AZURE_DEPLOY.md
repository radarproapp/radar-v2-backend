# Deploy Radar V2 to Azure App Service

## Prerequisites
- Azure account (free tier works): https://azure.microsoft.com/free
- Your MongoDB Atlas connection string — **rotate the database user's password in Atlas first** (a prior version of this repo committed it in plaintext); grab the new connection string from Atlas → Database → Connect
- Git (for pushing code)

---

## Step 1: Create Azure App Service

1. Go to https://portal.azure.com
2. Click **Create a resource** → **Web App**
3. Fill in:
   - **Subscription**: Your subscription
   - **Resource Group**: Create new → `radar-rg`
   - **Name**: `radar-v2-app` (or your choice)
   - **Publish**: **Docker Container** (recommended) OR **Code**
   - **Runtime stack**: **.NET 10** (if using Code) or **Docker** (if using Dockerfile)
   - **Operating System**: **Linux** (cheaper, faster)
   - **Region**: Choose closest to your users
4. Click **Review + Create** → **Create**

---

## Step 2: Configure Environment Variables

After the App Service is created:

1. Go to your App Service → **Settings** → **Environment variables**
2. Add these:

| Name | Value |
|---|---|
| `MongoDB__ConnectionString` | `mongodb+srv://<user>:<password>@<cluster>.mongodb.net/?appName=Cluster0` (get from Atlas → Database → Connect; rotate the credential first, see note below) |
| `MongoDB__DatabaseName` | `radar` |
| `OpenRouter__ApiKey` | (your OpenRouter API key) |
| `OpenRouter__BaseUrl` | `https://openrouter.ai/api/v1` |
| `OpenRouter__Model` | `deepseek/deepseek-chat` |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

**Note**: Azure uses `__` (double underscore) for nested config keys.

---

## Step 3: Deploy

### Option A: GitHub Actions (Recommended)

1. Push your code to GitHub:
   ```bash
   cd RadarV2
   git init
   git add .
   git commit -m "Initial deployment"
   git remote add origin https://github.com/YOUR_USERNAME/radar-v2.git
   git push -u origin main
   ```

2. In Azure Portal → App Service → **Deployment Center**
3. Select **GitHub** → authorize → select your repo
4. Azure will auto-deploy on every push

### Option B: Azure CLI (Direct publish)

```bash
# Login to Azure
az login

# Create resource group
az group create --name radar-rg --location eastus

# Create App Service Plan (free tier)
az appservice plan create --name radar-plan --resource-group radar-rg --is-linux --sku F1

# Create Web App
az webapp create --name radar-v2-app --resource-group radar-rg --plan radar-plan --runtime "DOTNET|10.0"

# Set environment variables
az webapp config appsettings set --name radar-v2-app --resource-group radar-rg \
  --settings \
    "MongoDB__ConnectionString=<your rotated Atlas connection string>" \
    "MongoDB__DatabaseName=radar" \
    "OpenRouter__ApiKey=YOUR_KEY" \
    "ASPNETCORE_ENVIRONMENT=Production"

# Publish
dotnet publish -c Release -o ./publish
cd publish
zip -r ../deploy.zip .
cd ..
az webapp deploy --name radar-v2-app --resource-group radar-rg --src-path deploy.zip --type zip
```

---

## Step 4: Verify

1. Go to `https://radar-v2-app.azurewebsites.net`
2. You should see the Radar landing page
3. Navigate to `/onboarding` to test the full flow
4. Check MongoDB Atlas → your `radar` database should have collections created automatically

---

## Step 5: Custom Domain (Optional)

1. In Azure Portal → App Service → **Custom domains**
2. Add your domain (e.g., `radar.app`)
3. Configure DNS: Add a CNAME record pointing to `radar-v2-app.azurewebsites.net`
4. Enable HTTPS (free managed certificate)

---

## Troubleshooting

- **502 Bad Bay Gateway**: App hasn't started. Check logs at App Service → **Monitoring** → **Log stream**
- **Connection string not working**: Ensure `__` (double underscore) not `:` in env vars
- **Cold start**: Free tier spins down after inactivity. First request takes ~30s
- **Logs**: App Service → **Monitoring** → **Log stream** for real-time logs
