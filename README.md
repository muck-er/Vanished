<div align="center">

<img src="Vanished-Client/Vanished/Resources/Logo/LogoWithoutText.png" alt="Vanished logo" width="200">

# Vanished

### Private communication with end-to-end encryption

A private desktop messaging application featuring a **.NET/Avalonia** client, a **FastAPI** API, a **PostgreSQL** database, **Redis** caching, and infrastructure deployed with **Docker**, **Caddy**, **Cloudflare**, and **TLS**.

<br>

![Status](https://img.shields.io/badge/status-functional%20prototype-00AEEF?style=for-the-badge)
![PAP](https://img.shields.io/badge/PAP-2023--2026-0F172A?style=for-the-badge)
![License](https://img.shields.io/badge/license-Apache%202.0-2563EB?style=for-the-badge)
![Security](https://img.shields.io/badge/security-E2EE-10B981?style=for-the-badge)

<br>

[Website](https://vanished.pt) · [License](#license)

</div>

---

## Table of Contents

- [About](#about)
- [Highlights](#highlights)
- [Demo](#demo)
- [Technology Stack](#technology-stack)
- [Architecture](#architecture)
- [Security](#security)
- [Testing and Validation](#testing-and-validation)
- [Technical Report](#technical-report)
- [Roadmap](#roadmap)
- [License](#license)
- [Author](#author)

---

## About

**Vanished** is a private communication application developed as part of the **Professional Aptitude Project** for the **Computer Programming** course.

The project demonstrates a client-server architecture in which message content is protected on the client before being sent to the API. The server manages authentication, sessions, devices, conversations, and message delivery, but only handles encrypted envelopes.

The application was built with a focus on:

- privacy;
- application security;
- end-to-end encryption;
- separation of responsibilities;
- real-world infrastructure.

---

## Highlights

| Area | Implementation |
|---|---|
| Desktop client | C# · .NET 8 · Avalonia UI |
| Backend | Python · FastAPI · Uvicorn |
| Database | PostgreSQL |
| Cache and temporary state | Redis |
| Real-time communication | WebSocket |
| Reverse proxy | Caddy |
| DNS and public-facing layer | Cloudflare |
| Secure transport | HTTPS/TLS |
| End-to-end encryption | X25519 · HKDF-SHA256 · AES-256-GCM |
| Request signing | Ed25519 |
| Key derivation | Argon2id |
| Source code license | Apache License 2.0 |
| Brand and visual materials | All rights reserved |

---

## Demo

| Resource | Address |
|---|---|
| Public website | https://vanished.pt |

The website presents the project, its main features, the security approach, the frequently asked questions section, and the application downloads.

---

## Technology Stack

<div align="center">

![C#](https://img.shields.io/badge/C%23-239120?style=flat-square&logo=csharp&logoColor=white)
![.NET](https://img.shields.io/badge/.NET%208-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![Avalonia](https://img.shields.io/badge/Avalonia%20UI-7B2CBF?style=flat-square)
![Python](https://img.shields.io/badge/Python-3776AB?style=flat-square&logo=python&logoColor=white)
![FastAPI](https://img.shields.io/badge/FastAPI-009688?style=flat-square&logo=fastapi&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-4169E1?style=flat-square&logo=postgresql&logoColor=white)
![Redis](https://img.shields.io/badge/Redis-DC382D?style=flat-square&logo=redis&logoColor=white)
![Docker](https://img.shields.io/badge/Docker%20Compose-2496ED?style=flat-square&logo=docker&logoColor=white)
![Caddy](https://img.shields.io/badge/Caddy-1F88C0?style=flat-square)
![Cloudflare](https://img.shields.io/badge/Cloudflare-F38020?style=flat-square&logo=cloudflare&logoColor=white)

</div>

### Desktop Client

- C#;
- .NET 8;
- Avalonia UI 11;
- AXAML/XAML;
- Newtonsoft.Json;
- Nsec.Cryptography;
- Portable.BouncyCastle;
- Konscious Argon2;
- System.IdentityModel.Tokens.Jwt.

### API Server

- Python;
- FastAPI;
- Uvicorn;
- SQLAlchemy;
- PyJWT;
- pyotp;
- cryptography;
- argon2-cffi;
- WebSocket.

### Infrastructure

- PostgreSQL;
- Redis;
- Docker Compose;
- Caddy;
- Cloudflare;
- UFW Firewall;
- TLS/HTTPS;
- Brevo.

---

## Architecture

Vanished is divided into three main layers.

### Desktop Client

The client handles the user interface, local authentication, session management, communication with the API, cryptographic material generation, message encryption, and the signing of critical requests.

### API Server

The API validates users, devices, permissions, sessions, and signed requests. It also manages conversations, encrypted messages, WebSocket events, and data persistence.

### Public Infrastructure

The infrastructure uses Docker Compose to organise the services, Caddy as the reverse proxy, Cloudflare for DNS and proxy services, and TLS/HTTPS for secure transport.

---

## Security

The project applies security by design throughout its architecture. Messages are encrypted on the client before being sent to the API. The server never receives the content of private messages in plaintext.

### Main Mechanisms

| Mechanism | Purpose |
|---|---|
| X25519 | Key agreement between the sender and recipient |
| HKDF-SHA256 | Symmetric key derivation from the shared secret |
| AES-256-GCM | Authenticated message encryption |
| Ed25519 | Digital signing of requests by each device |
| Argon2id | Local key derivation |
| JWT HS256 | Authenticated sessions |
| Hashed refresh tokens | Reduced exposure in the event of a database breach |
| Redis | Rate limiting and anti-replay protection |
| TLS/HTTPS | Secure transport between the client and server |
| UFW Firewall | Control over server exposure |

### Applied Principles

- Private keys are kept on the client;
- The server is limited to encrypted envelopes and necessary metadata;
- Separation between the client, API, database, cache, and reverse proxy;
- Critical requests are signed by the device;
- Abuse prevention through rate limiting;
- Protection against request replay attacks;
- Security logging without exposing secrets.

---

## Testing and Validation

The project was validated through manual testing, database inspection, analysis of critical workflows, infrastructure verification, and a review of the implemented security mechanisms.

### Tested Workflows

- Account creation;
- Email verification;
- Login;
- Session management;
- Device registration;
- User search;
- Conversation requests;
- Sending and receiving messages;
- WebSocket communication;
- Encrypted envelope persistence;
- HTTPS configuration;
- Reverse proxy;
- Firewall;
- Docker Compose;
- Project presentation website.

---

## Technical Report

The Professional Aptitude Project report documents the project in detail.

It includes:

- introduction and motivation;
- state of the art;
- project context;
- application architecture;
- account creation, login, and messaging workflows;
- entity-relationship model;
- functional and non-functional requirements;
- technologies used;
- cryptography and application security;
- methodology;
- implementation;
- server, domain, Cloudflare, and firewall configuration;
- conclusion;
- bibliographic references;
- technical appendices.

---

## Roadmap

- Implementation of a complete Double Ratchet protocol;
- External audit of the cryptographic code;
- Support for encrypted attachments;
- Mobile application;
- Encrypted voice and video calls;
- Complete automated testing;
- Digital signing of installers;
- Public checksum system for releases;
- Operational monitoring dashboard.

---

## License

The source code is licensed under the **Apache License 2.0**.

The **Vanished** name, logo, Professional Aptitude Project report, screenshots, diagrams, images, and all other visual materials are the property of the author and remain protected under **all rights reserved**.

See:

- [`LICENSE`](LICENSE)
- [`NOTICE`](NOTICE)

---

## Author

<div align="center">

**António João Carvalho Silva**

**Computer Programming** Course  
**Escola Secundária Frei Heitor Pinto**  
Professional Aptitude Project · **2023–2026**

</div>

---

<div align="center">

**Vanished** — private communication with E2EE.

</div>
