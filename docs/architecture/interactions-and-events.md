# Interações e Eventos

## 1. Objetivo

Este documento define como os módulos do Virtual Employee Platform interagem entre si, quais comunicações são síncronas ou assíncronas, quais eventos são publicados e consumidos e onde devem existir mecanismos de idempotência, retry e auditoria.

A meta é preservar limites claros entre módulos sem introduzir complexidade distribuída desnecessária no MVP.

## 2. Princípios de comunicação

- chamadas síncronas são usadas quando o resultado é necessário imediatamente para continuar o fluxo;
- eventos são usados para efeitos colaterais, desacoplamento e processamento posterior;
- dentro do Monólito Modular, eventos podem ser inicialmente processados em memória após commit;
- processamento crítico que dependa de provedor externo deve ser tratado de forma resiliente e idempotente;
- nenhum consumidor deve assumir que um evento será entregue exatamente uma vez;
- toda operação pertencente a um estabelecimento deve carregar `TenantId`;
- eventos devem carregar `CorrelationId` e identificadores suficientes para rastreamento;
- contratos de eventos devem ser versionáveis.

## 3. Tipos de interação

### Síncrona

Usada quando o chamador precisa da resposta imediatamente.

Exemplos:

- consultar serviços;
- consultar preço;
- consultar disponibilidade;
- criar appointment;
- criar PaymentOrder;
- consultar resumo financeiro solicitado pelo usuário;
- validar permissão.

### Assíncrona

Usada quando a operação pode ocorrer depois da resposta principal ou depende de processamento externo.

Exemplos:

- envio de confirmação;
- lembretes;
- retries de mensagem;
- processamento de webhook;
- atualização analítica;
- estorno;
- metering;
- auditoria.

## 4. Envelope padrão de evento

Eventos internos e futuros eventos de integração devem seguir um envelope conceitual semelhante a:

```text
EventId
EventType
EventVersion
OccurredAt
TenantId
CorrelationId
CausationId
AggregateId
Payload
```

Regras:

- `EventId` deve ser único;
- `CorrelationId` conecta todo o fluxo;
- `CausationId` identifica o evento/comando que originou o atual;
- `TenantId` é obrigatório para dados multi-tenant;
- consumidores devem registrar eventos já processados quando necessário.

## 5. Fluxo principal de agendamento

### 5.1 Descoberta de serviço e disponibilidade

```text
Cliente WhatsApp
      ↓
Messaging
      ↓
Conversation
      ↓
AI Gateway ──→ interpreta intenção
      ↓
Conversation
      ├──→ Services.GetService / ListServices
      └──→ Scheduling.GetAvailableSlots
```

Tipo predominante: **síncrono**.

Motivo: o cliente espera uma resposta imediata na conversa.

A IA apenas interpreta o pedido; preço e disponibilidade vêm do backend.

### 5.2 Criação do appointment

```text
Conversation
      ↓
Scheduling.CreateAppointment()
      ↓
Appointment PENDING
      ↓
AppointmentCreated
```

A criação do appointment é síncrona.

`AppointmentCreated` é publicado somente após a transação do Scheduling ser persistida com sucesso.

Possíveis consumidores:

- Usage Metering;
- Audit & Observability;
- Analytics;
- Conversation, quando necessário para atualização de estado.

Neste ponto, o appointment ainda não é considerado pago.

## 6. Fluxo de pagamento

### 6.1 Criação da ordem de pagamento

Após o cliente escolher o serviço e o horário:

```text
Conversation
      ↓
Payments.CreatePaymentOrder(AppointmentId)
      ├──→ Scheduling.GetAppointment()
      └──→ Services.GetServicePrice()
      ↓
PaymentOrder CREATED/PENDING
      ↓
Gateway cria checkout seguro
      ↓
Checkout URL
      ↓
Messaging envia link ao cliente
```

Tipo predominante: **síncrono até a geração do checkout**.

Regra de valor:

> Payments deve obter o valor oficial do serviço através do backend. Nunca aceitar o valor final fornecido por IA ou cliente.

Chave de idempotência sugerida:

```text
TenantId + AppointmentId + PaymentPurpose
```

Objetivo: evitar múltiplas cobranças acidentais para o mesmo appointment.

### 6.2 Confirmação do gateway

O redirect do cliente não confirma pagamento.

Fluxo confiável:

```text
Gateway
   ↓
Webhook Endpoint
   ↓
Validar autenticidade/assinatura
   ↓
Persistir recebimento do webhook
   ↓
Deduplicar
   ↓
Processar status do gateway
   ↓
Payments confirma Payment
   ↓
PaymentConfirmed
```

O processamento deve ser **idempotente**.

O identificador do evento do gateway deve ser registrado para impedir processamento duplicado.

### 6.3 Consumidores de PaymentConfirmed

`PaymentConfirmed` pode ser consumido por:

#### Scheduling

Transiciona o appointment de `PENDING` para `CONFIRMED`, desde que as regras de consistência sejam satisfeitas.

Depois publica:

```text
AppointmentConfirmed
```

#### Messaging

Não deve consumir diretamente o webhook externo.

Deve reagir ao evento interno apropriado, preferencialmente `AppointmentConfirmed`, para enviar a confirmação ao cliente.

#### Analytics

Atualiza projeções/receita conforme o modelo analítico.

#### Usage Metering

Registra uma operação de pagamento.

#### Audit & Observability

Registra a transição financeira e operacional.

## 7. Confirmação de agendamento

Fluxo:

```text
PaymentConfirmed
      ↓
Scheduling
      ↓
Appointment CONFIRMED
      ↓
AppointmentConfirmed
      ├──→ Messaging
      ├──→ Analytics
      ├──→ Audit
      └──→ Reminder Scheduler
```

O envio da mensagem é **assíncrono**.

Falha no WhatsApp não deve desfazer o pagamento nem o appointment confirmado.

## 8. Lembretes

Ao confirmar um appointment:

```text
AppointmentConfirmed
      ↓
Reminder Scheduler
      ↓
Agenda job para aproximadamente 24h antes
      ↓
Worker / Function
      ↓
Verifica estado atual do appointment
      ↓
Messaging.SendInteractiveMessage()
```

Antes de enviar o lembrete, o job deve verificar se o appointment continua válido.

Não enviar se estiver:

- CANCELLED_BY_CLIENT;
- CANCELLED_BY_BUSINESS;
- RESCHEDULED para outro horário;
- COMPLETED.

Chave de idempotência sugerida:

```text
TenantId + AppointmentId + ReminderType + ScheduledDate
```

## 9. Reagendamento solicitado pelo cliente

```text
Cliente
  ↓
Conversation
  ↓
AI interpreta intenção
  ↓
Scheduling.GetAvailableSlots()
  ↓
Cliente escolhe novo horário
  ↓
Scheduling.RescheduleAppointment()
  ↓
AppointmentRescheduled
```

A alteração do appointment é **síncrona** após confirmação da escolha.

Consumidores de `AppointmentRescheduled`:

- Messaging → confirmação da nova data;
- Reminder Scheduler → cancelar/invalidar lembrete anterior e agendar novo;
- Analytics → atualizar projeções;
- Audit.

O pagamento existente continua associado ao appointment quando a política permitir.

## 10. Cancelamento pelo cliente

```text
Cliente
  ↓
Conversation
  ↓
Scheduling.CancelAppointment()
  ↓
AppointmentCancelled
```

O Scheduling determina o cancelamento operacional.

Após isso, a aplicação avalia a política financeira.

```text
AppointmentCancelled
      ↓
Refund Policy Evaluation
      ↓
Se aplicável → Refunds.RequestRefund()
```

Importante:

> Cancelamento e estorno são processos relacionados, mas não são a mesma operação.

O appointment pode estar cancelado enquanto o refund ainda está sendo processado.

## 11. Fluxo de estorno

### 11.1 Solicitação

```text
Refund Policy / ação autorizada do proprietário
      ↓
Refunds.RequestRefund()
      ↓
REFUND_REQUESTED
      ↓
RefundRequested
```

### 11.2 Processamento externo

```text
RefundRequested
      ↓
Worker
      ↓
Refunds valida Payment
      ↓
Payment Gateway
      ↓
REFUND_PROCESSING
```

O processamento é **assíncrono**.

Chave de idempotência sugerida:

```text
TenantId + PaymentId + RefundType
```

Como o MVP só permite estorno integral, deve existir no máximo um estorno financeiro bem-sucedido por pagamento.

### 11.3 Resultado

Sucesso:

```text
Gateway confirma refund
      ↓
Refunds
      ↓
REFUNDED
      ↓
RefundCompleted
```

Falha:

```text
REFUND_FAILED
      ↓
RefundFailed
```

Consumidores de `RefundCompleted`:

- Messaging → informar cliente;
- Analytics → ajustar receita;
- Audit & Observability;
- Usage Metering.

Nunca informar “estornado” antes da confirmação do gateway.

## 12. Indisponibilidade do estabelecimento/profissional

Exemplo administrativo:

> “Não vou trabalhar amanhã à tarde.”

Fluxo:

```text
Business WhatsApp Admin
      ↓
Conversation
      ↓
AI interpreta período/profissional
      ↓
Scheduling.GetAffectedAppointments()
      ↓
Retorna impacto
      ↓
Proprietário confirma bloqueio
      ↓
Scheduling.BlockSchedule()
      ↓
ScheduleBlocked
```

Após o bloqueio:

```text
ScheduleBlocked
      ↓
ProcessAffectedAppointments
      ↓
Para cada cliente afetado
      ↓
Messaging oferece alternativas
```

O sistema **não move appointments automaticamente sem consentimento do cliente**.

Cada resposta do cliente volta ao fluxo normal de reagendamento ou cancelamento.

## 13. Comandos administrativos via WhatsApp

Exemplo:

> “Quanto faturei hoje?”

Fluxo:

```text
Business WhatsApp
      ↓
Conversation
      ↓
AI Gateway interpreta intenção
      ↓
Analytics.GetFinancialSummary(today)
      ↓
Resultado estruturado
      ↓
Conversation gera resposta
      ↓
Messaging
```

Tipo: **síncrono**.

A IA não soma valores nem calcula indicadores.

Exemplo de comando com alteração:

> “Bloqueia amanhã depois das 14h.”

Fluxo deve incluir confirmação antes de alterações de alto impacto quando apropriado.

## 14. SaaS Billing

Billing possui fluxo separado de Payments.

```text
Billing Scheduler / Billing Provider
      ↓
Cobrança da assinatura
      ↓
Webhook Billing
      ↓
Billing processa status
      ↓
SubscriptionActivated
ou
SubscriptionPaymentFailed
ou
SubscriptionSuspended
```

Possíveis consumidores:

### Identity & Tenants

Atualiza capacidade de uso do tenant quando uma assinatura for suspensa/cancelada conforme regra comercial.

### Messaging

Envia avisos administrativos ao assinante.

### Audit

Registra alteração de assinatura.

Billing não publica eventos para Payments e não manipula pagamentos dos clientes do estabelecimento.

## 15. Usage Metering

Uso técnico deve preferencialmente ser registrado de forma assíncrona para não aumentar latência dos fluxos principais.

Exemplos:

```text
AIRequestCompleted
      ↓
Usage Metering
```

```text
MessageSent
      ↓
Usage Metering
```

```text
AppointmentCreated
      ↓
Usage Metering
```

O metering não deve interromper a operação principal se houver falha temporária.

## 16. Analytics

No MVP, Analytics pode usar uma combinação de:

- consultas otimizadas sobre dados permitidos por contrato;
- projeções próprias atualizadas por eventos.

Preferência evolutiva:

```text
Domain Events
      ↓
Analytics Projection
      ↓
Read Models
```

Eventos relevantes:

- AppointmentCreated;
- AppointmentConfirmed;
- AppointmentRescheduled;
- AppointmentCancelled;
- PaymentConfirmed;
- RefundCompleted;
- NoShowRegistered;
- AppointmentCompleted.

Analytics deve tolerar consistência eventual para dashboards não críticos.

## 17. Matriz inicial de eventos

| Evento | Publisher | Consumers principais | Processamento |
|---|---|---|---|
| AppointmentCreated | Scheduling | Analytics, Usage, Audit | Async preferido |
| AppointmentConfirmed | Scheduling | Messaging, Reminder Scheduler, Analytics, Audit | Async |
| AppointmentRescheduled | Scheduling | Messaging, Reminder Scheduler, Analytics, Audit | Async |
| AppointmentCancelled | Scheduling | Refund Policy, Messaging, Analytics, Audit | Async |
| ScheduleBlocked | Scheduling | Affected Appointment Processor, Audit | Async |
| PaymentCreated | Payments | Audit, Usage | Async |
| PaymentConfirmed | Payments | Scheduling, Analytics, Usage, Audit | Async handler crítico |
| PaymentFailed | Payments | Conversation/Messaging, Audit | Async |
| RefundRequested | Refunds | Refund Worker, Audit | Async |
| RefundCompleted | Refunds | Messaging, Analytics, Usage, Audit | Async |
| RefundFailed | Refunds | Messaging/Admin Notification, Audit | Async |
| SubscriptionActivated | Billing | Identity/Tenants, Audit | Async |
| SubscriptionPaymentFailed | Billing | Messaging, Audit | Async |
| SubscriptionSuspended | Billing | Identity/Tenants, Messaging, Audit | Async |

## 18. Sincronismo por fluxo

### Deve ser síncrono no MVP

- autenticação/autorização;
- consulta de tenant;
- consulta de serviço;
- consulta de preço;
- consulta de disponibilidade;
- criação de appointment;
- reagendamento;
- cancelamento operacional;
- criação de PaymentOrder;
- geração do checkout;
- consultas administrativas interativas;
- analytics solicitado em conversa quando disponível em read model.

### Deve ser assíncrono no MVP

- mensagens decorrentes de eventos;
- lembretes;
- retries;
- processamento resiliente de webhook;
- refund;
- metering;
- auditoria não bloqueante;
- atualização de projeções analíticas.

## 19. Webhooks

Webhooks são fronteiras externas e devem ter tratamento específico.

Fluxo padrão:

```text
HTTP Endpoint
   ↓
Validar origem/assinatura
   ↓
Normalizar payload
   ↓
Registrar WebhookInbox
   ↓
Responder rapidamente ao provedor
   ↓
Processar de forma resiliente
```

Campos conceituais de `WebhookInbox`:

```text
WebhookInboxId
Provider
ProviderEventId
TenantId
ReceivedAt
PayloadReference / Payload seguro
Status
ProcessedAt
RetryCount
LastError
```

`Provider + ProviderEventId` deve possuir unicidade lógica.

Não manter dados sensíveis desnecessários no payload persistido.

## 20. Outbox Pattern

Como o MVP utiliza Monólito Modular e banco PostgreSQL único, eventos críticos devem evoluir para **Transactional Outbox** quando começarmos a depender de entrega assíncrona confiável.

Fluxo:

```text
Transação do domínio
   ├── atualiza agregado
   └── grava OutboxMessage

Commit
   ↓
Outbox Processor
   ↓
Publica/processa evento
   ↓
Marca como processado
```

Vantagem:

Evita o cenário em que o banco confirma a mudança, mas o evento se perde entre o commit e a publicação.

No início do desenvolvimento, eventos puramente internos e não críticos podem usar dispatch após commit, mas Payments, Refunds, Billing e Messaging devem ser tratados como candidatos prioritários ao Outbox.

## 21. Inbox / deduplicação

Consumidores críticos devem usar um mecanismo de Inbox ou registro de processamento.

Estrutura conceitual:

```text
Consumer
EventId
ProcessedAt
Result
```

Antes de executar:

```text
if EventId already processed:
    ignore safely
```

Prioridade para:

- PaymentConfirmed;
- RefundCompleted;
- webhooks financeiros;
- Subscription events;
- comandos que possam gerar cobrança duplicada.

## 22. Retry e Dead Letter

Falhas transitórias devem utilizar retry com backoff.

Exemplos:

- WhatsApp indisponível;
- gateway temporariamente indisponível;
- timeout de LLM;
- erro transitório de rede.

Após limite configurado de tentativas, a mensagem/job deve ir para estado de falha operacional ou futura Dead Letter Queue quando Service Bus for adotado.

Nunca fazer retry automático irrestrito de operações financeiras sem idempotência.

## 23. Ordem de consistência

### Consistência forte/local

Usar dentro do módulo para:

- reservar slot;
- impedir double booking;
- criar/alterar appointment;
- registrar estado de Payment;
- registrar estado de Refund.

### Consistência eventual

Aceitável para:

- dashboard analítico;
- contadores de uso;
- mensagens;
- telemetria;
- projeções de leitura.

## 24. Concorrência crítica de agenda

Duas pessoas podem tentar reservar o mesmo horário simultaneamente.

O Scheduling deve garantir a restrição no backend/banco, e não confiar apenas na disponibilidade exibida anteriormente.

Fluxo:

```text
GetAvailableSlots() → mostra 15:00

Cliente A tenta reservar 15:00
Cliente B tenta reservar 15:00

CreateAppointment()
      ↓
validação transacional/constraint
      ↓
apenas um vence
```

O cliente que perder a disputa recebe novos horários disponíveis.

## 25. Timeouts e limites

Chamadas externas devem possuir:

- timeout explícito;
- retry apenas quando seguro;
- circuit breaker onde fizer sentido;
- correlation ID;
- métricas de latência e falha.

A indisponibilidade do LLM não deve comprometer dados financeiros ou corromper estado de negócio.

## 26. Fluxo crítico completo do MVP

```text
Cliente
  ↓
WhatsApp
  ↓
Messaging
  ↓
Conversation
  ↓
AI Gateway
  ↓
Services + Scheduling
  ↓
CreateAppointment(PENDING)
  ↓
Payments.CreatePaymentOrder
  ↓
Checkout seguro
  ↓
Cliente paga
  ↓
Gateway Webhook
  ↓
Webhook Inbox
  ↓
Payments
  ↓
PaymentConfirmed
  ↓
Scheduling
  ↓
AppointmentConfirmed
  ├──→ Messaging → confirmação
  ├──→ Reminder Scheduler
  ├──→ Analytics
  ├──→ Usage Metering
  └──→ Audit
```

Essa cadeia representa o principal fluxo econômico e operacional da plataforma.

## 27. Decisões para o MVP

- Modular Monolith permanece como unidade principal de deploy;
- PostgreSQL é o banco principal;
- não usar Service Bus por padrão no início;
- preparar interfaces para introdução futura de broker;
- usar workers/Functions para jobs e integrações assíncronas;
- usar idempotência desde o início em operações financeiras;
- implementar Webhook Inbox para pagamentos e billing;
- adotar Outbox prioritariamente para eventos financeiros e mensagens críticas quando a implementação assíncrona iniciar;
- nenhuma confirmação financeira baseada apenas no frontend;
- nenhuma IA executa diretamente operações de domínio ou financeiras.

## 28. Próxima etapa arquitetural

Com módulos, dependências e interações definidos, os próximos artefatos recomendados são:

1. C4 System Context;
2. C4 Container;
3. modelo de domínio inicial, começando por Scheduling;
4. modelo de dados/ERD;
5. fluxos detalhados de Payment, Refund e Conversation;
6. ADRs que formalizam as decisões arquiteturais já tomadas.

O próximo passo recomendado é formalizar as decisões principais em ADRs e, em seguida, produzir os diagramas C4 oficiais alinhados a estes documentos.
