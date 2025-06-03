# 📁 Scripts/Certs — Pre-generated Certificate Folder

This folder is used to optionally include pre-generated OpenVPN certificates and keys during the Docker image build process.

## What goes here?

You can place the following files inside this folder:

- `ca.crt` – Certificate Authority (CA) certificate  
- `server.crt` – Server certificate  
- `server.key` – Server private key  
- `ta.key` – TLS authentication key  
- `crl.pem` – Certificate Revocation List (optional)

## How does it work?

- If these files are present, the container will use them **instead of generating new ones**.
- If the folder is empty or the files are missing, the container will **automatically generate new certificates** at runtime using EasyRSA.

## Why is this useful?

This mechanism allows:

- Faster container startup with already trusted certs  
- Certificate reuse between rebuilds or deployments 
- Fine-grained control over OpenVPN credentials in CI/CD pipelines