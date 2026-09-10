# C4 — System Context

## 1. Objetivo

Este documento descreve o **C4 System Context (Nível 1)** da Virtual Employee Platform.

O objetivo deste nível é mostrar o sistema como uma única unidade, as pessoas que interagem com ele e os sistemas externos dos quais depende. Detalhes internos como módulos, API, banco de dados, workers e infraestrutura pertencem aos níveis seguintes do C4.

## Visão Circular da Arquitetura

A visão circular complementa os diagramas C4 e apresenta a arquitetura em camadas, do núcleo operacional ao ecossistema externo.

![Virtual Employee Platform — Visão Circular da Arquitetura](../../diagrams/virtual-employee-platform-circular-architecture.jpg)

**Do centro para fora:**

1. **Domínio do Negócio** — Scheduling Engine / Funcionário Virtual, Services, Professionals, Customers, Appointments, Availability e regras de negócio.
2. **Capacidades da Plataforma** — Conversation, AI Gateway, Messaging, Payments, Refunds, Billing, Analytics, Usage Metering e Audit.
3. **Plataforma & Infraestrutura** — ASP.NET Core, PostgreSQL, Workers/Functions, Blob Storage, observabilidade, segurança e Multi-Tenancy.
4. **Canais & Experiência** — WhatsApp-first, PWA/Web e experiência do Cliente Final e do Estabelecimento.
5. **Ecossistema & Integrações Externas** — Meta/WhatsApp Business Platform, OpenAI/LLM, Mercado Pago/Gateway e SaaS Billing Provider.

> **Regra arquitetural:** IA interpreta → Backend valida → Domínio executa → Gateway processa → Webhook confirma.

> **Regra financeira:** Customer Payments e SaaS Billing são fluxos independentes. A plataforma não custodia os valores dos serviços e não cobra taxa própria por agendamento.

## 2. Sistema principal

### Virtual Employee Platform

**Tipo:** Software System

Plataforma SaaS Multi-Tenant que funciona como um **funcionário virtual** para pequenos negócios e prestadores de serviços.

Responsabilidades de alto nível:

- atender clientes pelo WhatsApp;
- responder informações sobre negócio e serviços;
- consultar preços e disponibilidade;
- criar agendamentos;
- reagendar e cancelar;
- conduzir pagamentos de serviços através de checkout seguro;
- acompanhar confirmação de pagamentos;
- processar regras de cancelamento e estorno;
- enviar confirmações e lembretes;
- auxiliar o estabelecimento em situações de indisponibilidade;
- fornecer operação administrativa pelo WhatsApp;
- disponibilizar gestão através de PWA/Web;
- apresentar indicadores financeiros e operacionais;
- controlar assinatura e acesso do estabelecimento à plataforma.

## 3. Pessoas

### 3.1 Cliente Final

**Tipo:** Person

Pessoa que contrata os serviços oferecidos pelo estabelecimento.

Interage principalmente através do WhatsApp para consultar serviços, preços e horários disponíveis, selecionar profissional quando aplicável, agendar, reagendar, cancelar, acessar checkout seguro e receber confirmações e lembretes.

O cliente final não precisa instalar aplicativo nem criar conta para utilizar o fluxo principal.

### 3.2 Assinante / Estabelecimento

**Tipo:** Person

Pequeno negócio ou profissional prestador de serviços que contrata a Virtual Employee Platform.

Utiliza a plataforma para realizar onboarding, configurar o estabelecimento, cadastrar serviços, preços e profissionais, definir horários e regras, administrar a agenda, consultar clientes, bloquear períodos, tratar indisponibilidades, consultar indicadores financeiros e administrar sua assinatura SaaS.

Os principais canais do assinante são PWA/Web mobile-first e WhatsApp para comandos administrativos selecionados.

## 4. Sistemas externos

### 4.1 Meta / WhatsApp Business Platform

Canal oficial de comunicação utilizado entre clientes, estabelecimentos e a Virtual Employee Platform, incluindo mensagens, templates, eventos e webhooks.

### 4.2 Provedor de IA / LLM

Exemplo inicial: OpenAI ou provedor equivalente. Utilizado para interpretação de linguagem natural e experiência conversacional. O provedor de IA não é autoridade de negócio e não acessa diretamente o banco de dados, define preços, inventa disponibilidade, confirma pagamentos ou executa estornos.

### 4.3 Gateway de Pagamento de Serviços

Exemplo candidato: Mercado Pago ou provedor equivalente. Responsável pelo checkout seguro, Pix, cartões, processamento da transação, status, webhooks e estornos. O gateway processa pagamentos realizados pelo cliente final em favor do estabelecimento.

### 4.4 Conta/Saldo do Estabelecimento no Provedor

Representa a conta, carteira ou saldo do estabelecimento no provedor de pagamento, conforme o modelo do gateway escolhido. É o destino econômico dos valores pagos pelos clientes pelos serviços.

### 4.5 SaaS Billing Provider

Responsável pelo processamento da assinatura paga pelo estabelecimento à Virtual Employee Platform. O modelo inicial prevê mensal por cartão recorrente ou Pix e anual com compromisso de 12 meses por cartão recorrente, sem boleto.

### 4.6 Conta Bancária PJ da Plataforma

Conta empresarial da Virtual Employee Platform que recebe a receita proveniente das assinaturas SaaS após processamento pelo provedor de Billing. Não recebe automaticamente os valores pagos pelos clientes aos estabelecimentos pelos serviços.

## 5. Relacionamentos do System Context

| Origem | Destino | Relacionamento |
|---|---|---|
| Cliente Final | Meta / WhatsApp Business Platform | Conversa pelo WhatsApp |
| Meta / WhatsApp Business Platform | Virtual Employee Platform | Entrega mensagens, eventos e webhooks |
| Virtual Employee Platform | Meta / WhatsApp Business Platform | Envia respostas, confirmações, lembretes e mensagens interativas |
| Assinante / Estabelecimento | Virtual Employee Platform | Configura e administra o negócio via PWA/Web |
| Assinante / Estabelecimento | Meta / WhatsApp Business Platform | Envia comandos administrativos via WhatsApp |
| Virtual Employee Platform | Provedor de IA / LLM | Solicita interpretação de linguagem natural |
| Provedor de IA / LLM | Virtual Employee Platform | Retorna intenção/dados interpretados; não executa domínio |
| Virtual Employee Platform | Gateway de Pagamento de Serviços | Cria checkout e solicita operações financeiras autorizadas |
| Gateway de Pagamento de Serviços | Virtual Employee Platform | Envia status e webhooks financeiros |
| Gateway de Pagamento de Serviços | Conta/Saldo do Estabelecimento no Provedor | Liquida/mantém os valores dos serviços conforme regras do provedor |
| Assinante / Estabelecimento | SaaS Billing Provider | Paga a assinatura da plataforma |
| Virtual Employee Platform | SaaS Billing Provider | Gerencia ciclo/status de assinatura através de integração |
| SaaS Billing Provider | Virtual Employee Platform | Envia status e webhooks de Billing |
| SaaS Billing Provider | Conta Bancária PJ da Plataforma | Liquida a receita da assinatura SaaS conforme regras do provedor |

## 6. Fluxos financeiros separados

### Customer Payments

```text
Cliente Final
      ↓
Gateway de Pagamento
      ↓
Conta/Saldo do Estabelecimento no Provedor
```

A plataforma orquestra a operação, mas não cobra taxa própria por appointment e não custodia os valores dos serviços.

### SaaS Billing

```text
Assinante / Estabelecimento
      ↓
SaaS Billing Provider
      ↓
Conta Bancária PJ da Plataforma
```

A assinatura SaaS não é descontada das vendas do estabelecimento.

## 7. Fluxo conceitual de decisão

Para operações comuns:

```text
Usuário
   ↓
Canal
   ↓
Virtual Employee Platform
   ↓
IA interpreta intenção
   ↓
Backend valida
   ↓
Domínio executa
```

Para operações financeiras:

```text
IA interpreta
   ↓
Usuário autoriza quando necessário
   ↓
Backend valida
   ↓
Domínio executa
   ↓
Gateway processa
   ↓
Webhook confirma
```

## 8. Boundary do sistema

### Dentro da Virtual Employee Platform

Pertencem ao sistema a experiência conversacional, regras de negócio, agenda, catálogo de serviços, gestão de profissionais e clientes, orquestração de pagamentos, regras de refund, analytics, assinatura SaaS, metering, auditoria e PWA/Web do assinante.

### Fora da Virtual Employee Platform

São dependências externas a infraestrutura/canal WhatsApp da Meta, modelos/provedores de IA, gateway financeiro dos serviços, provedor de cobrança SaaS, contas/saldos financeiros dos estabelecimentos e conta bancária PJ da plataforma.

## 9. Restrições arquiteturais visíveis neste nível

- WhatsApp-first para o cliente final;
- PWA mobile-first para o assinante;
- Multi-Tenant;
- IA sem autoridade de negócio;
- checkout seguro externo;
- confirmação financeira por webhook/API confiável;
- separação entre Customer Payments e SaaS Billing;
- plataforma sem custódia dos valores dos serviços;
- sem taxa própria por agendamento.

## 10. Relação com os demais diagramas

```text
Visão Circular da Arquitetura
        ↓
C4 System Context
        ↓
C4 Container
        ↓
Module Architecture
        ↓
Domain / Component Diagrams
```

A **Visão Circular** fornece uma leitura executiva das camadas da solução. O **System Context** explica quem usa o sistema e de quais sistemas externos ele depende. O **C4 Container** detalha as grandes unidades executáveis e de armazenamento, como PWA, Backend API, Workers e PostgreSQL.

Os diagramas **Macro Architecture** e **High-Level Architecture** existentes no Eraser continuam válidos como visões complementares e não são substituídos pelo C4.

## 11. Workspace visual oficial

Os diagramas oficiais da arquitetura são mantidos no mesmo workspace do Eraser:

https://app.eraser.io/workspace/UcBRZVFG8H5U6A3qDZwZ

Diagramas já existentes incluem Macro Architecture, High-Level Architecture, C4 Container e Visão Circular da Arquitetura.
