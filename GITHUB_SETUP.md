# GitHub Repository Setup - Quick Guide

Your local Git repository is ready! Follow these steps to publish to GitHub:

## Step 1: Create a New Repository on GitHub

1. Go to https://github.com/new
2. Fill in the repository details:
   - **Repository name**: `dtce-api` (or your preferred name)
   - **Description**: `Document Template & Context Extractor API - A .NET-based platform for extracting structured templates and contextual metadata from documents`
   - **Visibility**: Choose Public or Private
   - **Important**: DO NOT initialize with README, .gitignore, or license (we already have these)
3. Click "Create repository"

## Step 2: Push Your Code to GitHub

After creating the repository, GitHub will show you commands. Use these commands in PowerShell:

### Option A: Using HTTPS (Recommended for first-time setup)

```powershell
git remote add origin https://github.com/YOUR_USERNAME/YOUR_REPO_NAME.git
git branch -M main
git push -u origin main
```

Replace:
- `YOUR_USERNAME` with your GitHub username
- `YOUR_REPO_NAME` with the repository name you created

### Option B: Using SSH (If you have SSH keys set up)

```powershell
git remote add origin git@github.com:YOUR_USERNAME/YOUR_REPO_NAME.git
git branch -M main
git push -u origin main
```

## Step 3: Verify

After pushing, visit your repository on GitHub to verify all files are uploaded.

## Troubleshooting

### Authentication Issues

If you get authentication errors when pushing:

1. **For HTTPS**: GitHub no longer accepts passwords. You'll need to:
   - Use a Personal Access Token (PAT) instead of your password
   - Create one at: https://github.com/settings/tokens
   - Use the token as your password when prompted

2. **For SSH**: Make sure you have SSH keys set up:
   - Check: `ssh -T git@github.com`
   - If not set up, follow: https://docs.github.com/en/authentication/connecting-to-github-with-ssh

### Branch Name

If you get an error about branch name, the default branch might be `master` instead of `main`. You can either:
- Rename: `git branch -M main` (already done above)
- Or use: `git push -u origin master` if your GitHub default is `master`

## Next Steps

Once your code is on GitHub:
- ✅ Your repository is live
- ✅ You can share the repository URL
- ✅ You can set up CI/CD workflows
- ✅ You can collaborate with others

## Repository Status

- ✅ Git repository initialized
- ✅ .gitignore created
- ✅ README.md created
- ✅ Initial commit created (365 files)
- ⏳ Ready to push to GitHub

