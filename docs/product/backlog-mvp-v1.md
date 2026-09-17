# Backlog MVP v1

> Status: EM CONSTRUÇÃO — execução orientada por vertical slices
> Data: 2026-09-17

## 1. Objetivo

Converter visão, arquitetura, domínio, ERD, contratos de API e Wireframes v1 aprovados em trabalho executável. Hierarquia adotada:

`EPIC -> Feature -> User Story -> Task`

O backlog deve priorizar entrega de valor e prova das regras críticas do produto. Evitar infraestrutura ou abstrações sem necessidade real do MVP.

## 2. Mapa de EPICs

| # | EPIC | Objetivo |
|---|---|---|
| 01 | Fundação da Plataforma | Solution .NET, PostgreSQL, padrões técnicos, testes e observabilidade básica |
| 02 | Identity & Multi-Tenancy | Conta Customer, autenticação, verificação e isolamento Tenant |
| 03 | Negócio & Onboarding | Business, Location e configuração inicial |
| 04 | Serviços & Profissionais | Serviços, combos, profissionais e elegibilidade |
| 05 | Scheduling | Disponibilidade, slots, Appointment, bloqueios e concorrência |
| 06 | Payments & Refunds | Pagamento integral, PaymentAttempt, webhook e refund |
| 07 | WhatsApp | Canal operacional do Client e mensagens |
| 08 | Funcionário Virtual / IA | Conversation Engine, AI Gateway e tools |
| 09 | Dashboard & Analytics | Métricas financeiras, operacionais, comerciais e retenção |
| 10 | SaaS Billing | Assinatura mensal/anual da plataforma |
| 11 | Segurança, LGPD & Auditoria | Controles transversais e preparação de compliance |
| 12 | Pilot Readiness | E2E, performance, deploy, operação e preparação do piloto |

---

# EPIC 01 — Fundação da Plataforma

## Objetivo

Criar uma base executável simples e consistente para os primeiros vertical slices, seguindo Modular Monolith e evitando microservices/Kubernetes/Service Bus/Redis prematuros.

## Feature 01.1 — Solution .NET

### US-01.1.1 — Estrutura inicial

**Como** desenvolvedor da plataforma  
**Quero** uma solution organizada por responsabilidades  
**Para** evoluir o produto sem acoplamento desnecessário.

**Critérios de aceite**
- solution compila localmente;
- projetos possuem referências somente nas direções permitidas;
- API executa e expõe health endpoint;
- estrutura preparada para módulos do Modular Monolith sem criar microservices.

**Tasks**
- [ ] Criar `VirtualEmployee.sln`
- [ ] Criar `src/VirtualEmployee.Api`
- [ ] Criar `src/VirtualEmployee.Application`
- [ ] Criar `src/VirtualEmployee.Domain`
- [ ] Criar `src/VirtualEmployee.Infrastructure`
- [ ] Criar `tests/VirtualEmployee.Domain.Tests`
- [ ] Criar `tests/VirtualEmployee.Application.Tests`
- [ ] Criar `tests/VirtualEmployee.IntegrationTests`
- [ ] Criar `tests/VirtualEmployee.ArchitectureTests`
- [ ] Configurar referências entre projetos
- [ ] Criar `Directory.Build.props`
- [ ] Habilitar nullable e warnings relevantes
- [ ] Criar health endpoint mínimo

## Feature 01.2 — PostgreSQL local

### US-01.2.1 — Ambiente reproduzível

**Como** desenvolvedor  
**Quero** iniciar PostgreSQL localmente de forma reproduzível  
**Para** executar migrations e integration tests contra o mesmo engine escolhido para produção.

**Critérios de aceite**
- PostgreSQL sobe via Docker Compose;
- aplicação conecta por configuração externa;
- secrets não são commitados;
- integração pode usar PostgreSQL real, não banco in-memory para invariantes críticos.

**Tasks**
- [ ] Criar `docker-compose.yml`
- [ ] Configurar PostgreSQL
- [ ] Configurar connection string por ambiente
- [ ] Adicionar EF Core + provider PostgreSQL
- [ ] Criar `DbContext` inicial
- [ ] Configurar factory/design-time quando necessária
- [ ] Documentar comandos locais no README

## Feature 01.3 — API baseline

### US-01.3.1 — Convenções HTTP

**Como** consumidor da API  
**Quero** respostas e erros previsíveis  
**Para** integrar PWA, WhatsApp e demais adapters com contratos consistentes.

**Critérios de aceite**
- Problem Details segue RFC 9457;
- erros incluem `code` e `correlationId`;
- API não retorna stack trace/SQL/secrets;
- status codes seguem `contracts-v1.md`.

**Tasks**
- [ ] Configurar exception handling global
- [ ] Implementar Problem Details baseline
- [ ] Implementar CorrelationId
- [ ] Configurar validação estrutural de requests
- [ ] Configurar OpenAPI
- [ ] Preparar versionamento `/api/v1`

## Feature 01.4 — Observabilidade básica

### US-01.4.1 — Logs estruturados e seguros

**Como** operador da plataforma  
**Quero** rastrear uma requisição sem expor dados sensíveis  
**Para** diagnosticar falhas com segurança.

**Critérios de aceite**
- logs estruturados incluem CorrelationId;
- TenantId é incluído quando contexto autenticado existir;
- PII, OTP, secrets, cartão e payload bruto sensível não são registrados;
- App Insights pode ser conectado posteriormente sem alterar domínio.

**Tasks**
- [ ] Configurar logging estruturado
- [ ] Middleware de CorrelationId
- [ ] Definir política inicial de redaction
- [ ] Testar ausência de secrets/PII críticos em erros comuns

## Feature 01.5 — Test baseline

### US-01.5.1 — Pirâmide de testes do MVP

**Como** equipe de engenharia  
**Quero** uma baseline automatizada  
**Para** proteger regras de domínio, isolamento, concorrência e idempotência.

**Tasks**
- [ ] Configurar xUnit
- [ ] Configurar unit tests de domínio
- [ ] Configurar integration tests com PostgreSQL real
- [ ] Criar infraestrutura reutilizável para testes de API
- [ ] Preparar Architecture Tests para dependências entre camadas
- [ ] Adicionar execução dos testes ao pipeline quando CI for criado

## Definition of Done — EPIC 01

- Solution compila e executa;
- PostgreSQL local reproduzível;
- API baseline disponível;
- testes executam;
- logs/correlation básicos ativos;
- nenhuma infraestrutura distribuída adicionada sem necessidade comprovada.

---

# EPIC 02 — Identity & Multi-Tenancy

## Objetivo

Permitir que o Customer crie e proteja sua conta, tenha e-mail e WhatsApp verificados e opere sempre dentro de um Tenant confiável, impedindo descoberta/acesso cross-tenant.

## Feature 02.1 — Cadastro do Customer

### US-02.1.1 — Criar conta

**Como** proprietário de um estabelecimento  
**Quero** criar minha conta com nome, e-mail e WhatsApp  
**Para** iniciar a configuração do meu funcionário virtual.

**Critérios de aceite**
- e-mail é normalizado/validado;
- telefone/WhatsApp é normalizado para formato canônico;
- conta não é considerada plenamente ativada antes das verificações obrigatórias;
- TenantId não é recebido do frontend como autoridade.

**Tasks**
- [ ] Definir integração/adaptação do Identity Provider
- [ ] Implementar fluxo de signup
- [ ] Persistir vínculo entre identidade e Customer/Tenant
- [ ] Normalizar e-mail e telefone
- [ ] Testar duplicidades e erros de cadastro

## Feature 02.2 — Verificação de e-mail e WhatsApp

### US-02.2.1 — Verificar e-mail

**Como** Customer  
**Quero** verificar meu e-mail no primeiro acesso  
**Para** comprovar que controlo o canal cadastrado.

**Critérios de aceite**
- verificação obrigatória antes da ativação;
- estado verificado disponível para a aplicação;
- token/código não aparece em logs/LLM;
- troca futura de e-mail exige nova verificação.

**Tasks**
- [ ] Integrar fluxo de verificação do Identity Provider
- [ ] Registrar/consultar estado de verificação
- [ ] Implementar tratamento de expiração/reenvio
- [ ] Testar token inválido/expirado/reutilizado

### US-02.2.2 — Verificar WhatsApp

**Como** Customer  
**Quero** verificar meu número de WhatsApp por OTP  
**Para** comprovar posse do canal usado pela conta.

**Critérios de aceite**
- OTP é curto, expirável e de uso único;
- proteção contra brute force/replay;
- OTP não é armazenado em texto puro nem registrado em logs;
- telefone alterado perde estado verificado até nova validação;
- sucesso registra conceitualmente `PhoneVerifiedAt`.

**Tasks**
- [ ] Definir provider/adapter de envio OTP
- [ ] Implementar solicitação de código
- [ ] Implementar validação de código
- [ ] Implementar expiração e limite de tentativas
- [ ] Implementar rate limit de reenvio
- [ ] Testar replay, brute force e código expirado

## Feature 02.3 — Login e autenticação reforçada

### US-02.3.1 — Login seguro

**Como** Customer verificado  
**Quero** acessar a PWA com segurança  
**Para** gerenciar meu negócio sem repetir dois códigos em todo acesso normal.

**Critérios de aceite**
- autenticação delegada ao Identity Provider quando aplicável;
- aplicação recebe identidade autenticada confiável;
- sessões/tokens seguem práticas seguras do provider;
- informações sensíveis não são expostas ao frontend além do necessário.

**Tasks**
- [ ] Integrar login
- [ ] Configurar validação de token/claims na API
- [ ] Implementar logout
- [ ] Testar acesso anônimo a endpoints protegidos

### US-02.3.2 — Step-up authentication

**Como** Customer  
**Quero** confirmação adicional em situações sensíveis  
**Para** reduzir risco de tomada de conta.

**Cenários baseline**
- novo dispositivo quando sinalizado pelo mecanismo de identidade;
- recuperação de conta;
- alteração de e-mail;
- alteração de WhatsApp;
- operação administrativa crítica quando definida.

**Tasks**
- [ ] Definir mecanismo de step-up suportado pelo Identity Provider
- [ ] Integrar desafio adicional
- [ ] Exigir revalidação ao trocar contato verificado
- [ ] Auditar eventos críticos de segurança

## Feature 02.4 — Tenant Context

### US-02.4.1 — Resolver Tenant confiável

**Como** backend  
**Quero** resolver TenantId a partir da identidade/contexto confiável  
**Para** nunca depender de TenantId enviado pelo cliente como autoridade.

**Critérios de aceite**
- TenantId deriva do contexto autenticado;
- endpoints tenant-owned não aceitam TenantId arbitrário como autoridade;
- queries tenant-owned são filtradas pelo TenantContext;
- comandos validam ownership das referências.

**Tasks**
- [ ] Implementar `ITenantContext`
- [ ] Resolver Tenant a partir da identidade autenticada
- [ ] Configurar Global Query Filters onde aplicável
- [ ] Implementar validações tenant-aware
- [ ] Criar helpers/policies de autorização necessárias

## Feature 02.5 — Isolamento cross-tenant

### US-02.5.1 — Impedir acesso entre Tenants

**Como** plataforma multi-tenant  
**Quero** impedir leitura, atualização, referência ou descoberta de dados de outro Tenant  
**Para** garantir isolamento de segurança.

**Critérios de aceite**
- recurso de outro Tenant não é retornado;
- resposta externa usa `404 RESOURCE_NOT_FOUND` quando necessário para evitar disclosure;
- FK/relacionamentos críticos preservam barreira tenant-aware conforme ERD;
- mesmo CPF/CNPJ pode existir isoladamente em Tenants diferentes;
- testes automatizados cobrem read/update/reference/discovery cross-tenant.

**Tasks**
- [ ] Criar integration tests Tenant A x Tenant B
- [ ] Testar leitura cross-tenant
- [ ] Testar update cross-tenant
- [ ] Testar referência de entidade de outro Tenant
- [ ] Testar ausência de descoberta por filtros/listagens
- [ ] Validar barreiras PostgreSQL previstas no ERD

## Feature 02.6 — Segurança e acesso na PWA

### US-02.6.1 — Exibir estado de segurança

**Como** Customer  
**Quero** visualizar meus canais verificados e configurações de acesso  
**Para** entender o estado de proteção da minha conta.

**Critérios de aceite**
- e-mail e WhatsApp aparecem mascarados quando apropriado;
- status verificado é exibido;
- acesso a alteração de senha/credencial segue capacidade do Identity Provider;
- dispositivos/sessões só são exibidos se houver suporte real do provider, sem inventar informação.

**Tasks**
- [ ] Criar contrato de leitura do perfil de segurança
- [ ] Expor estados verificados
- [ ] Integrar ações suportadas pelo provider
- [ ] Implementar tela conforme Wireframes v1

## Definition of Done — EPIC 02

- Customer consegue criar conta;
- e-mail e WhatsApp são verificados antes da ativação;
- login funciona sem exigir dois códigos em toda sessão normal;
- step-up existe para cenários sensíveis suportados;
- TenantContext é server-side e confiável;
- testes provam isolamento cross-tenant;
- OTP/PII/secrets não vazam em logs.

---

# 3. Ordem de implementação por Vertical Slice

A ordem de EPIC não determina execução integral sequencial. O primeiro marco técnico atravessa Fundação, Identity/Tenant, Negócio, Catálogo e Scheduling:

```text
Solution + PostgreSQL
        ↓
Identity / TenantContext
        ↓
Business / Location
        ↓
Service / Professional
        ↓
AvailabilityRule
        ↓
POST /availability/slots/search
        ↓
POST /appointments
        ↓
PENDING
        ↓
Teste concorrente
        ↓
1 x 201 Created
1 x 409 SLOT_UNAVAILABLE
```

Segundo slice:

`Appointment PENDING -> Payment -> PaymentAttempt -> Gateway -> Webhook -> Appointment CONFIRMED`

Terceiro slice:

`WhatsApp -> Conversation Engine -> AI Gateway -> backend tools -> Scheduling/Payments`

## 4. Próximo detalhamento

Detalhar EPIC 03 — Negócio & Onboarding, EPIC 04 — Serviços & Profissionais e EPIC 05 — Scheduling, priorizando somente histórias necessárias ao primeiro vertical slice.
