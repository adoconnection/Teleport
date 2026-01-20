# 🚀 Teleport

**The Fast, Secure, and Simple File Transfer Utility**

Transfer files between machines with blazing-fast speeds and military-grade encryption. No cloud dependencies. No complicated setup. Just pure, efficient file transfers.

---

## ✨ Why Teleport?

- 🎯 **Dead Simple** - One command to upload, one command to download. That's it.
- ⚡ **Lightning-Fast** - Optimized chunked streaming for massive file support
- 🔒 **Military-Grade Encryption** - AES-256-GCM ensures your files are always protected
- 🚫 **No Cloud, No Lock-in** - Self-hosted. Your data stays under your control.
- 🌍 **Works Everywhere** - Windows, Linux, macOS on x86, x64, ARM
- 🐳 **Container-Ready** - Runs as Docker container for easy deployment
- 📊 **Progress Tracking** - Real-time feedback on file transfers

---

## 🎯 Use Cases

- **Dev Environment Sync** - Quickly sync code and build artifacts between machines
- **Backup & Archive** - Secure backups without cloud vendor lock-in
- **CI/CD Pipelines** - Artifact storage and distribution
- **Secure Data Transfer** - Transfer sensitive files with confidence
- **Offline Collaboration** - Share files on private networks
- **Build Systems** - Cache and distribute compiled binaries

---

## 🎬 Quick Start (3 Steps)

### 1️⃣ Start the Server

```bash
# Using Docker (recommended)
docker-compose up -d
```

The server is now running and ready to accept files.

### 2️⃣ Upload a File

```bash
teleport upload my-file.zip my-slot
```

### 3️⃣ Download It Elsewhere

```bash
teleport download my-slot my-file.zip
```

That's it! 🎉

---

## 📋 Command Reference

```bash
# Upload a file or folder
teleport upload <file> <slot>

# Download a file
teleport download <slot> <file>

# List files in a slot
teleport list <slot>

# Create a directory
teleport mkdir <slot> <path>

# Clear local cache
teleport clean
```

---

## 🖥️ Platform Support

Teleport works on everything:

| | Support |
|----------|---------|
| **Operating Systems** | Windows (7+), Linux, macOS (10.15+) |
| **Architectures** | x86-64 (x64), ARM64 (Apple Silicon, Raspberry Pi), x86 (32-bit) |
| **Server** | Docker containers on any system |

No external dependencies—just download and run!

---

## 🔒 Security

Your files are protected with:

- **AES-256-GCM Encryption** - Industry-standard authenticated encryption
- **Secure Key Derivation** - SHA-256 based key derivation
- **Per-Transfer Verification** - Every transfer is authenticated
- **Real-time Encryption** - Files encrypted during transfer, never stored unencrypted

---

## 📦 Deployment

### Quick Start: Docker Compose

```bash
docker-compose up -d
```

The `docker-compose.yml` handles all configuration automatically.

### Manual Docker Deployment

```bash
# Build the image
docker build -f Server/Dockerfile -t teleport-server .

# Run the container
docker run -d \
  -p 5000:5000 \
  -e TELEPORT_AccessKey="your-key" \
  -v teleport-storage:/app/Store \
  teleport-server
```

### Local Development

```bash
# Build the project
dotnet build

# Run Server
dotnet run --project Server/

# Run Client
dotnet run --project Client/ -- upload file.zip my-slot
```

---

## ⚙️ Configuration

Configure using environment variables (prefix: `TELEPORT_`):

| Variable | Description | Default |
|----------|-------------|---------|
| `TELEPORT_AccessKey` | Server access key for authentication | Auto-generated |
| `TELEPORT_StorePath` | Directory for storing files | `./Store` |

---

## 🏗️ Architecture (Technical Details)

### Components

- **Client** - CLI application for uploading/downloading files with progress tracking
- **Server** - ASP.NET Core REST API for file management and transfer
- **Shared** - Cryptographic protocols and shared data structures

### Protocol

Teleport uses a custom binary protocol with:
- Command-based operations (Upload, Download, List, MkDir, Reset)
- Slot-based file organization
- Chunked transfer for handling files of any size
- Built-in encryption at the protocol level

### Performance Characteristics

- **Large File Support** - No practical size limits on file transfers
- **Optimized Streaming** - 1MB chunk-based streaming for efficient memory usage
- **Concurrent Operations** - Support for parallel uploads and downloads
- **Built-in Compression** - Support for tar and zip formats

---

## 🤝 Contributing

Have ideas? Found a bug? We'd love your contributions!

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

---

## 📄 License

This project is licensed under the MIT License - see the LICENSE file for details.

---

## 🙋 Support

Got questions? Run into issues? Open an issue in the repository.

---

**Transfer files. Securely. Instantly. That's Teleport.** 🌐⚡🔒
