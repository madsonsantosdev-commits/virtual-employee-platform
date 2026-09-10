# Módulos e Limites Arquiteturais

## 1. Objetivo

Este documento define os limites dos módulos do Virtual Employee Platform, suas responsabilidades, dependências permitidas e regras de comunicação dentro do Monólito Modular.

O objetivo é evitar acoplamento indevido e manter cada capacidade de negócio coesa, testável e preparada para evolução futura.

## 2. Regra geral de dependências

Os módulos devem se comunicar por contratos explícitos.

Princípios:

- nenhum módulo acessa diretamente as tabelas internas de outro módulo;
- nenhum módulo referencia SDK externo pertencente a outro módulo;
- comunicação síncrona deve ocorrer por interfaces/contratos de aplicação;
- comunicação assíncrona deve ocorrer por eventos internos quando houver benefício real;
- integrações externas ficam atrás de adapters;
- TenantId deve acompanhar toda operação pertencente a um estabelecimento;
- dependências cíclicas entre módulos são proibidas.

## 3. Visão dos módulos

### 3.1 Identity & Tenants

Responsável por:

- autenticação;
- autorização;
- usuários do assinante;
- tenants;
- associação usuário-tenant;
- papéis e permissões;
- contexto do tenant atual.

Não é responsável por:

- regras de agenda;
- dados de clientes finais;
- pagamento de serviços;
- lógica de assinatura SaaS além de status necessário para autorização de uso.

Dependências permitidas:

- infraestrutura de autenticação;
- Audit & Observability.

Expõe contratos como:

- `GetCurrentTenant()`
- `GetCurrentUser()`
- `HasPermission()`
- `IsTenantActive()`

---

### 3.2 Business Profile

Responsável por:

- dados cadastrais do estabelecimento;
- tipo de negócio;
- endereço e contatos;
- horários gerais de funcionamento;
- políticas gerais configuráveis;
- configurações operacionais do tenant.

Não é responsável por:

- catálogo detalhado de serviços;
- agenda;
- pagamento;
- assinatura SaaS.

Dependências permitidas:

- Identity & Tenants;
- Audit & Observability.

---

### 3.3 Services

Responsável por:

- catálogo de serviços;
- nome e descrição;
- preço oficial;
- duração;
- status ativo/inativo;
- regras específicas de serviço;
- elegibilidade básica de profissionais quando modelada pelo domínio.

Regra crítica:

> O valor cobrado pelo serviço deve sempre ser obtido do catálogo persistido pelo backend.

A IA nunca define, altera ou negocia preço.

Dependências permitidas:

- Identity & Tenants;
- Business Profile;
- Audit & Observability.

Expõe contratos como:

- `GetService()`
- `ListServices()`
- `GetServicePrice()`
- `GetServiceDuration()`

---

### 3.4 Professionals

Responsável por:

- cadastro de profissionais;
- status ativo/inativo;
- vínculo com tenant;
- especialidades/serviços atendidos;
- jornada e indisponibilidades específicas quando aplicável.

Não é responsável por:

- criar appointments;
- confirmar disponibilidade final;
- calcular receita.

Dependências permitidas:

- Identity & Tenants;
- Business Profile;
- Services;
- Audit & Observability.

---

### 3.5 Customers

Responsável por:

- cadastro do cliente final;
- identificação por telefone/WhatsApp;
- dados mínimos necessários para atendimento;
- preferências permitidas;
- histórico cadastral básico.

Não é responsável por:

- histórico financeiro agregado;
- conversa da IA;
- agenda.

Dependências permitidas:

- Identity & Tenants;
- Audit & Observability.

---

### 3.6 Scheduling

É o **domínio operacional central** do produto.

Responsável por:

- disponibilidade;
- slots;
- appointments;
- bloqueios de agenda;
- criação de agendamento;
- reagendamento;
- cancelamento;
- conflito de horários;
- validação de serviço/profissional/data;
- identificação de appointments afetados por indisponibilidade;
- estados do appointment.

Estados iniciais previstos:

- PENDING
- CONFIRMED
- CONFIRMED_BY_CLIENT
- RESCHEDULE_REQUESTED
- RESCHEDULED
- CANCELLED_BY_CLIENT
- CANCELLED_BY_BUSINESS
- COMPLETED
- NO_SHOW

Scheduling pode consultar:

- Services;
- Professionals;
- Customers;
- Business Profile.

Scheduling não pode depender de:

- WhatsApp SDK;
- OpenAI SDK;
- Mercado Pago SDK;
- SaaS Billing Provider;
- implementação concreta de Messaging.

Expõe contratos como:

- `GetAvailableSlots()`
- `CreateAppointment()`
- `RescheduleAppointment()`
- `CancelAppointment()`
- `BlockSchedule()`
- `GetAffectedAppointments()`

Eventos possíveis:

- `AppointmentCreated`
- `AppointmentRescheduled`
- `AppointmentCancelled`
- `AppointmentConfirmed`
- `ScheduleBlocked`

---

### 3.7 Conversation

Responsável por:

- estado da conversa;
- contexto conversacional;
- fluxo de atendimento;
- intenção atual;
- orquestração das ferramentas disponíveis para a IA;
- controle da jornada do cliente pelo WhatsApp.

Estados conversacionais iniciais:

- NEW
- SERVICE_SELECTED
- DATE_REQUESTED
- SLOT_SELECTED
- AWAITING_CONFIRMATION
- AWAITING_PAYMENT
- BOOKED

Conversation pode chamar contratos de:

- Services;
- Scheduling;
- Customers;
- Payments;
- Business Profile;
- AI Gateway;
- Messaging.

Conversation não acessa banco de outros módulos nem executa regra financeira diretamente.

---

### 3.8 AI Gateway

Responsável por:

- integração com provedores de LLM;
- roteamento de modelos;
- prompts;
- contexto;
- controle de tokens;
- estimativa de custo;
- políticas e limites por tenant;
- fallback quando aplicável;
- telemetria de uso de IA.

Não é responsável por:

- disponibilidade;
- preço;
- persistência de appointments;
- confirmação de pagamento;
- execução de refund.

Regra:

> AI Gateway interpreta linguagem natural e retorna intenção/dados estruturados. A decisão final de negócio pertence aos módulos de domínio.

Dependências permitidas:

- Identity & Tenants;
- Usage Metering;
- Audit & Observability;
- adapters de LLM.

---

### 3.9 Messaging

Responsável por:

- envio de mensagens;
- recebimento normalizado de eventos dos canais;
- templates;
- retries de envio;
- status de entrega quando disponível;
- abstração do provedor WhatsApp.

Não é responsável por:

- decidir conteúdo de negócio;
- criar appointment;
- interpretar intenção com IA;
- confirmar pagamento.

Dependências permitidas:

- Identity & Tenants;
- Usage Metering;
- Audit & Observability;
- adapter Meta/WhatsApp.

Expõe contratos como:

- `SendMessage()`
- `SendTemplate()`
- `SendInteractiveMessage()`

---

### 3.10 Payments

Responsável pelo pagamento do **cliente final ao estabelecimento**.

Responsável por:

- PaymentOrder;
- criação de checkout;
- vínculo entre appointment e pagamento;
- estado do pagamento;
- integração com gateway;
- processamento de webhook financeiro;
- idempotência;
- validação do valor com base no serviço.

Estados sugeridos:

- CREATED
- PENDING
- PAID
- FAILED
- CANCELLED
- EXPIRED
- REFUNDED

Regra crítica:

> Um pagamento só é considerado confirmado após validação do gateway via webhook/API confiável.

Payments pode consultar:

- Scheduling;
- Services;
- Identity & Tenants.

Payments não pode:

- cobrar valor arbitrário recebido da IA;
- confiar em redirect do navegador como confirmação;
- armazenar dados brutos de cartão.

Eventos possíveis:

- `PaymentCreated`
- `PaymentConfirmed`
- `PaymentFailed`
- `PaymentExpired`

---

### 3.11 Refunds

Responsável por:

- solicitação de estorno;
- aplicação das regras permitidas;
- chamada ao gateway;
- idempotência;
- acompanhamento assíncrono;
- confirmação do resultado.

Estados:

- REFUND_REQUESTED
- REFUND_PROCESSING
- REFUNDED
- REFUND_FAILED

No MVP:

- apenas estorno integral;
- sem estorno parcial.

Refunds pode consultar:

- Payments;
- Scheduling;
- Business Profile.

Refunds não deve ser executado diretamente por Conversation ou AI Gateway.

Eventos possíveis:

- `RefundRequested`
- `RefundCompleted`
- `RefundFailed`

---

### 3.12 Billing

Responsável pela cobrança da **assinatura SaaS do estabelecimento**.

Responsável por:

- Subscription;
- plano contratado;
- ciclo de cobrança;
- status da assinatura;
- cartão recorrente;
- Pix mensal quando aplicável;
- retries de cobrança;
- webhook do provedor de billing.

Modelo inicial:

- mensal: cartão recorrente ou Pix;
- anual/12 meses: cartão recorrente;
- sem boleto.

Billing é totalmente separado de Payments.

Regra:

> Billing nunca desconta a mensalidade das vendas do estabelecimento.

Dependências permitidas:

- Identity & Tenants;
- Audit & Observability;
- adapter do SaaS Billing Provider.

Eventos possíveis:

- `SubscriptionActivated`
- `SubscriptionPaymentFailed`
- `SubscriptionSuspended`
- `SubscriptionCancelled`

---

### 3.13 Analytics

Responsável por:

- consolidação de métricas;
- receita realizada;
- receita prevista;
- ticket médio;
- quantidade de appointments;
- cancelamentos;
- refunds;
- no-show;
- indicadores por profissional;
- indicadores por serviço;
- comparações entre períodos equivalentes;
- forecasting simples do MVP.

Analytics consome dados/contratos de:

- Scheduling;
- Payments;
- Refunds;
- Services;
- Professionals.

A IA não calcula números financeiros. Conversation/AI solicita informações ao Analytics e apenas apresenta a resposta.

Expõe contratos como:

- `GetFinancialSummary(period)`
- `GetRevenueByProfessional(period)`
- `GetRevenueByService(period)`
- `GetRevenueComparison(period)`
- `GetRevenueForecast(period)`

---

### 3.14 Usage Metering

Responsável por medir consumo técnico e econômico por tenant.

Métricas iniciais:

- WhatsAppInbound;
- WhatsAppOutbound;
- WhatsAppCost;
- AIRequests;
- InputTokens;
- OutputTokens;
- AICost;
- Appointments;
- Payments;
- Refunds.

Não decide plano comercial nem bloqueio automaticamente sem regra explícita do Billing/Platform.

---

### 3.15 Audit & Observability

Capacidade transversal responsável por:

- logs estruturados;
- métricas;
- traces;
- correlation IDs;
- trilha de auditoria;
- eventos de segurança;
- alterações relevantes de negócio;
- telemetria operacional.

Todos os módulos podem depender desta capacidade através de abstrações compartilhadas.

## 4. Dependências permitidas — visão resumida

```text
Identity/Tenants
      ↓
Business Profile
      ↓
Services ─────→ Professionals
   ↓               ↓
   └────────→ Scheduling ←──── Customers
                  ↑   ↑
                  │   │
Conversation ─────┘   └──── Payments ───→ Refunds
    │                        │
    ├──→ AI Gateway          └──→ Analytics
    └──→ Messaging                 ↑
                                   │
                         Services / Professionals

Billing ─────────────── separado de Payments

Usage Metering e Audit/Observability são capacidades transversais.
```

O desenho acima é conceitual. Não significa que todas as chamadas devam ser síncronas.

## 5. Regras de fronteira

### Regra 1 — sem acesso direto a tabelas de outro módulo

Exemplo proibido:

`ConversationDbContext` consultando diretamente `Appointments`.

Forma correta:

`Conversation → ISchedulingQueries.GetAvailableSlots()`

### Regra 2 — SDK externo não atravessa fronteira

Exemplo proibido:

`Scheduling` usando classes do SDK do Mercado Pago.

Forma correta:

`Payments → IPaymentGateway → MercadoPagoAdapter`

### Regra 3 — IA nunca é autoridade de negócio

Exemplo:

Cliente: “faz por 50?”

A IA pode interpretar a intenção, mas `Services.GetServicePrice()` continua sendo a fonte de verdade.

### Regra 4 — pagamento não confirma appointment sozinho sem regra de aplicação

`PaymentConfirmed` deve ser processado pela aplicação para transicionar o appointment conforme o fluxo definido.

### Regra 5 — eventos não substituem consistência local desnecessariamente

Dentro do mesmo módulo e transação, usar chamada direta.

Eventos são preferidos para efeitos colaterais desacoplados, como:

- lembretes;
- mensagens;
- analytics;
- auditoria;
- processamento assíncrono.

## 6. Shared Kernel

O Shared Kernel deve ser mínimo.

Pode conter apenas elementos realmente transversais, por exemplo:

- `TenantId`
- `EntityId`
- `Money`
- `Result`
- abstrações de clock;
- abstrações de eventos;
- correlation context.

Não colocar regras de domínio específicas no Shared Kernel.

## 7. Estrutura de código sugerida

```text
src/backend/

├── VirtualEmployee.Api/
├── VirtualEmployee.SharedKernel/
│
├── Modules/
│   ├── Identity/
│   ├── Business/
│   ├── Services/
│   ├── Professionals/
│   ├── Customers/
│   ├── Scheduling/
│   ├── Conversation/
│   ├── AI/
│   ├── Messaging/
│   ├── Payments/
│   ├── Refunds/
│   ├── Billing/
│   ├── Analytics/
│   └── UsageMetering/
│
└── Infrastructure/
```

Cada módulo deverá evoluir internamente para uma estrutura semelhante a:

```text
Scheduling/
├── Domain/
├── Application/
├── Infrastructure/
└── Contracts/
```

Sem criar projetos .NET separados para tudo antes de haver necessidade. A separação física deverá acompanhar a complexidade real do código.

## 8. Direção para comunicação síncrona e assíncrona

### Preferencialmente síncrono

- consultar serviço/preço;
- consultar disponibilidade;
- criar appointment;
- validar profissional;
- obter resumo financeiro solicitado pelo usuário;
- criar PaymentOrder antes de gerar checkout.

### Preferencialmente assíncrono

- envio de lembretes;
- mensagens pós-evento;
- retries;
- processamento robusto de webhooks;
- atualização de projeções analíticas;
- estornos que dependem do gateway;
- telemetria e metering.

Essas decisões serão detalhadas no próximo documento de interações e eventos.

## 9. Critérios para futura extração de um módulo

Um módulo só deverá virar serviço independente se houver evidência de necessidade, como:

- volume muito diferente do restante da aplicação;
- necessidade específica de escala;
- necessidade de isolamento de falhas;
- ciclo de deploy independente;
- requisitos de segurança diferenciados;
- equipe responsável independente;
- gargalo operacional comprovado.

Possíveis candidatos futuros:

- Messaging;
- AI Gateway;
- Notifications;
- Payment Webhook Processing.

Isso não é decisão para o MVP.

## 10. Próxima etapa

Com os limites dos módulos definidos, o próximo passo é criar o **mapa de interações**, documentando:

- chamadas síncronas;
- eventos de domínio/integração;
- publishers;
- consumers;
- jobs assíncronos;
- pontos de idempotência;
- fluxos críticos de Scheduling, Payment, Refund e Messaging.
