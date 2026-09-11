# Modelo de Domínio v1

> Status: Draft v1 — decisões funcionais aprovadas
> Data: 2026-09-11

## Objetivo

Definir o modelo de domínio inicial da Virtual Employee Platform antes da implementação física da solução .NET e PostgreSQL.

## Princípios

1. Multi-Tenant desde o primeiro dia.
2. Scheduling é o domínio operacional central.
3. IA interpreta; backend valida; domínio executa.
4. Pagamento só é confirmado por webhook/API confiável do gateway.
5. Preço e duração históricos são preservados por snapshots.
6. Appointments não são apagados fisicamente.
7. Cancelamento operacional e refund financeiro são processos separados.
8. Sem sinal, pagamento parcial, pagamento complementar ou refund parcial no MVP.
9. Double-booking é impedido por validação transacional + constraint PostgreSQL.
10. Um Appointment pode conter um ou mais serviços, mas possui um único Professional no MVP.

---

## Entidades principais

### Tenant
Fronteira lógica de isolamento de dados do assinante.

### Business
Representa o estabelecimento. Mantém timezone, política de cancelamento/refund e granularidade de início de agenda (`SlotIntervalMinutes`, default 15).

### Service
Catálogo comercial do estabelecimento.

Campos principais:
- `ServiceId`, `TenantId`, `BusinessId`
- `Name`, `Description`
- `ServiceType` (`SINGLE`, `COMBO`)
- `Price`, `DurationMinutes`
- `RequiresPayment`, `IsActive`

Regras:
- preço e duração são definidos pelo backend;
- combo possui preço e duração próprios, não derivados automaticamente dos componentes;
- alteração posterior não modifica Appointment histórico;
- no MVP, combo não contém outro combo.

### ServiceComponent
Descreve a composição comercial de um Service do tipo `COMBO`.

Campos:
- `TenantId`
- `ServiceId` (combo)
- `ComponentServiceId` (serviço simples)

Regras:
- não permite autorreferência;
- componentes descrevem a composição, mas não calculam preço/duração do combo;
- analytics do MVP contabiliza o combo como serviço comercial próprio.

### Professional
Profissional que executa serviços do estabelecimento.

### ProfessionalService
Matriz de capacidade **Professional × Service**.

Campos:
- `TenantId`, `ProfessionalId`, `ServiceId`
- `IsActive`
- `CustomDurationMinutes` opcional/futuro

Regra aprovada:
> Um profissional é elegível para um Appointment somente se estiver habilitado para **todos** os serviços selecionados.

Exemplo: João executa Corte; Arthur executa Corte e Barba. Para Corte, ambos são elegíveis. Para Corte + Barba, somente Arthur é elegível.

Não criar entidade `Skill` separada no MVP. `ProfessionalService` cumpre esse papel operacional.

### AvailabilityRule
Representa a agenda-base recorrente do profissional por dia da semana e horário local.

A configuração deve ser simples e reaproveitável. A aplicação pode copiar/reutilizar a configuração do período anterior para que o pequeno empresário altere apenas exceções.

Dia recorrente sem trabalho = ausência de AvailabilityRule para aquele período/dia.

### ScheduleBlock
Representa exceção/indisponibilidade específica de profissional ou estabelecimento inteiro (`ProfessionalId = null`).

Exemplos: folga pontual, médico, feriado, treinamento, fechamento excepcional.

Antes de confirmar um bloqueio com appointments afetados, o sistema apresenta impacto. Após confirmação, o funcionário virtual oferece alternativas aos clientes. **Nenhum cliente é movido automaticamente sem consentimento.**

### Customer
Cliente final. Telefone/WhatsApp é identificador operacional principal, mas não PK.

---

## Appointment — Aggregate Root

Principal agregado operacional do Scheduling.

Campos principais:
- `AppointmentId`, `TenantId`, `BusinessId`
- `CustomerId`
- `ProfessionalId`
- `StartsAt`, `EndsAt`
- `TotalPriceSnapshot`
- `TotalDurationMinutesSnapshot`
- `Status`
- `ReservationExpiresAt`
- `CancelledAt`, `CancellationReason`
- `CreatedAt`, `UpdatedAt`

### AppointmentItem
Um Appointment possui **1..N AppointmentItems**.

Campos:
- `AppointmentItemId`, `TenantId`, `AppointmentId`
- `ServiceId`
- `ServiceNameSnapshot`
- `PriceSnapshot`
- `DurationMinutesSnapshot`
- `CreatedAt`

Regra:
- no MVP, o mesmo Service não é repetido no mesmo Appointment; não há `quantity`.

### Totais

```text
SUM(AppointmentItems.PriceSnapshot)
        -> Appointment.TotalPriceSnapshot
        -> Payment.Amount

SUM(AppointmentItems.DurationMinutesSnapshot)
        -> Appointment.TotalDurationMinutesSnapshot
        -> EndsAt = StartsAt + duração total
```

Se o item for um COMBO, usa-se preço e duração configurados no próprio combo, e não a soma de seus componentes.

### Fluxo de seleção

Preferência do MVP:

```text
Selecionar 1..N serviços
-> backend calcula preço/duração
-> opcionalmente sugere combo equivalente
-> encontra profissionais habilitados para TODOS os serviços
-> encontra janela contínua
-> cliente escolhe horário
-> CreateAppointment revalida tudo
-> Appointment PENDING
-> pagamento integral
-> webhook confiável
-> CONFIRMED
```

### Um único profissional

Um Appointment possui um único Professional no MVP. Não dividir Corte com João e Barba com Arthur dentro do mesmo Appointment.

### Status v1

- `PENDING`
- `CONFIRMED`
- `CONFIRMED_BY_CLIENT`
- `RESCHEDULE_REQUESTED`
- `CANCELLED_BY_CLIENT`
- `CANCELLED_BY_BUSINESS`
- `EXPIRED`
- `COMPLETED`
- `NO_SHOW`

`RESCHEDULED` é evento/histórico, não estado operacional permanente.

### Invariantes

1. `StartsAt < EndsAt`.
2. Appointment possui pelo menos um AppointmentItem.
3. `EndsAt - StartsAt` corresponde ao `TotalDurationMinutesSnapshot`.
4. `TotalPriceSnapshot` corresponde à soma dos itens.
5. Professional está habilitado para todos os Services selecionados.
6. O intervalo inteiro respeita agenda, bloqueios e appointments ativos.
7. Não há sobreposição ativa para o mesmo profissional.
8. `CreateAppointment` recalcula preço/duração e revalida disponibilidade; nunca confia em valores enviados pelo cliente.
9. Appointment pago só vira `CONFIRMED` após confirmação financeira confiável.
10. Alteração de composição após pagamento que mude valor não é suportada no MVP; usar fluxo controlado de cancelamento/novo booking.

### AppointmentHistory
Registra mudanças de status, horário, profissional e motivo, preservando auditoria e reagendamentos.

---

## Payment

Representa pagamento integral do Appointment.

Métodos: `PIX`, `CREDIT_CARD`, `DEBIT_CARD`.

Invariantes:
- `Payment.Amount == Appointment.TotalPriceSnapshot`;
- sem valor arbitrário enviado pelo cliente;
- sem sinal/pagamento parcial;
- checkout hospedado/tokenizado;
- confirmação somente por webhook/API confiável;
- `TenantId + AppointmentId + PaymentPurpose` idempotente logicamente.

## Refund

Refund integral e assíncrono. `Refund.Amount == Payment.Amount` no MVP. Nunca comunicar `REFUNDED` antes da confirmação do gateway.

## Conversation

Estado conversacional não substitui estado do Appointment. IA pode interpretar intenção e solicitar ferramentas estruturadas, mas não altera banco diretamente.

## Subscription

Billing SaaS separado de Customer Payments. Mensal: cartão recorrente ou Pix. Anual/12 meses: cartão recorrente. Sem boleto.

## UsageRecord

Mede AI requests/tokens/custo, WhatsApp inbound/outbound/custo, appointments, payments e refunds por tenant.

---

## Disponibilidade e agenda

Disponibilidade efetiva:

```text
Agenda-base recorrente (AvailabilityRule)
- exceções (ScheduleBlock)
- Appointments ativos
= janelas livres
```

Slots não são persistidos no MVP.

Para múltiplos serviços:

```text
Services selecionados
-> interseção de ProfessionalServices
-> duração total
-> janela contínua suficiente
```

A granularidade de 15 minutos define possíveis **inícios**, não a duração de um serviço.

### Reaproveitamento de agenda

A experiência deve permitir copiar/reutilizar o ciclo anterior e alterar somente exceções. Isso é comportamento da aplicação sobre AvailabilityRules/overrides, evitando obrigar o dono a reconstruir a agenda repetidamente.

### Imprevistos

```text
Administrador informa indisponibilidade
-> análise de impacto
-> confirmação do administrador
-> ScheduleBlock
-> cálculo de alternativas
-> mensagem aos clientes afetados
-> cliente escolhe
-> backend revalida
-> reagendamento
```

O sistema propõe automaticamente; o cliente decide.

---

## Concorrência

Consultar disponibilidade não garante vaga. `CreateAppointment` revalida tudo e a constraint PostgreSQL é a última barreira.

Em corrida, somente uma reserva é persistida. A tentativa perdedora recebe `SLOT_UNAVAILABLE` e alternativas atualizadas.

Mensagem padrão:

> **Desculpe, este horário acabou de ser preenchido. Escolha um dos horários disponíveis abaixo.**

---

## Relacionamentos conceituais

```text
Business -> Services
Service(COMBO) -> ServiceComponents -> Services(SINGLE)
Business -> Professionals
Professional <-> Service via ProfessionalService
Professional -> AvailabilityRules
Professional -> ScheduleBlocks
Business -> Customers
Customer -> Appointments
Professional -> Appointments
Appointment -> AppointmentItems -> Service
Appointment -> AppointmentHistory
Appointment -> Payment -> Refund
Tenant -> Conversations / Subscription / UsageRecords
```

---

## Regra central

> **IA interpreta -> Backend valida -> Domínio executa.**

Para operações financeiras:

> **IA interpreta -> Usuário autoriza quando necessário -> Backend valida -> Gateway processa -> Webhook confirma.**
