# Modelo de Domínio v1

> Status: Draft v1 — decisões funcionais aprovadas
> Data: 2026-09-14

## Objetivo
Definir o modelo de domínio inicial da Virtual Employee Platform antes da implementação física da solução .NET e PostgreSQL.

## Princípios
1. Multi-Tenant desde o primeiro dia.
2. Tenant é fronteira de propriedade, segurança e cobrança; não é sinônimo de unidade física.
3. Business representa o negócio/marca; Location representa a unidade operacional.
4. Scheduling é o domínio operacional central.
5. IA interpreta; backend valida; domínio executa.
6. Pagamento só é confirmado por webhook/API confiável do gateway.
7. Preço e duração históricos são preservados por snapshots.
8. Appointments não são apagados fisicamente.
9. Cancelamento operacional e refund financeiro são processos separados.
10. Sem sinal, pagamento parcial, pagamento complementar ou refund parcial no MVP.
11. Double-booking é impedido por validação transacional + constraint PostgreSQL.
12. Um Appointment pode conter um ou mais serviços, mas possui um único Professional no MVP.

---

## Estrutura organizacional

### Tenant
Fronteira lógica de propriedade, isolamento de dados, segurança e cobrança do assinante. Um Tenant não representa obrigatoriamente um estabelecimento físico.

### Business
Representa o negócio/marca operado pelo Tenant. Mantém políticas comerciais como cancelamento/refund e granularidade de início de agenda (`SlotIntervalMinutes`, default 15).

No MVP a experiência inicial será simples: 1 Tenant -> 1 Business -> 1 Location. O modelo, porém, suporta 1 Business -> N Locations para redes e unidades futuras.

### BusinessType
Classificação do negócio. Tipos oficiais da plataforma possuem escopo global; tipos customizados pertencem ao Tenant que os criou. A unicidade de tipos customizados é tenant-scoped, evitando conflito quando tenants diferentes usam o mesmo nome personalizado.

### LegalEntity
Representa a identidade jurídica/fiscal associada ao Business.

Campos conceituais:
- `TenantId`, `BusinessId`
- `EntityType` (`PERSON`, `COMPANY`)
- `DocumentType` (`CPF`, `CNPJ`)
- `DocumentNumber`
- `CountryCode`
- `LegalName`, `TradeName`
- `IsPrimary`, `IsActive`

Regras:
- CPF/CNPJ são normalizados e validados no backend;
- documento fiscal nunca é chave primária;
- documento completo não deve aparecer em logs;
- alteração de dados fiscais deve ser auditável;
- IA não recebe CPF/CNPJ sem necessidade operacional.

### Location
Representa uma unidade operacional física ou virtual do Business.

Mantém nome da unidade, telefone, endereço, país e timezone. Pode referenciar uma `LegalEntity`, permitindo que matriz e filial tenham entidades fiscais diferentes quando necessário.

Regra arquitetural aprovada:
> **Tenant = fronteira de propriedade, segurança e cobrança. Business = negócio/marca. Location = unidade operacional física ou virtual.**

Franquias não serão implementadas como módulo no MVP. Franqueados independentes podem futuramente possuir Tenants separados, preservando isolamento financeiro e de dados.

---

## Catálogo e profissionais

### Service
Catálogo comercial do estabelecimento.

Campos principais:
- `ServiceId`, `TenantId`, `BusinessId`
- `Name`, `Description`
- `ServiceType` (`SINGLE`, `COMBO`)
- `Price`, `DurationMinutes`
- `RequiresPayment`, `IsActive`

Regras: preço e duração são definidos pelo backend; combo possui preço/duração próprios; alteração posterior não modifica Appointment histórico; combo não contém outro combo no MVP.

O escopo final Business x Location de Service será fechado na revisão do próximo bloco do ERD.

### ServiceComponent
Descreve a composição comercial de um Service `COMBO`. Componentes devem ser serviços simples, não calculam automaticamente preço/duração do combo e não há combos aninhados no MVP.

### Professional
Profissional que executa serviços. O relacionamento definitivo com Location será fechado na revisão multi-unidade do bloco de catálogo/profissionais.

### ProfessionalService
Matriz de capacidade **Professional × Service**. Um profissional é elegível somente se estiver habilitado para todos os serviços selecionados. Não criar entidade `Skill` separada no MVP.

### AvailabilityRule
Agenda-base recorrente do profissional por dia da semana e horário local. A aplicação permite reaproveitar/copiar configuração e alterar exceções.

### ScheduleBlock
Exceção/indisponibilidade específica. Antes de bloquear período com appointments afetados, o sistema apresenta impacto; após confirmação, oferece alternativas aos clientes. Nenhum cliente é movido automaticamente sem consentimento.

### Customer
Cliente final. Telefone/WhatsApp é identificador operacional principal, mas não PK.

---

## Appointment — Aggregate Root
Principal agregado operacional do Scheduling.

Campos principais: `AppointmentId`, `TenantId`, `BusinessId`, `CustomerId`, `ProfessionalId`, `StartsAt`, `EndsAt`, `TotalPriceSnapshot`, `TotalDurationMinutesSnapshot`, `Status`, `ReservationExpiresAt`, dados de cancelamento e auditoria.

`LocationId` será incorporado ao Appointment quando fecharmos o escopo operacional multi-location do Scheduling.

### AppointmentItem
Um Appointment possui 1..N itens. Cada item preserva `ServiceId`, nome, preço e duração em snapshot. No MVP o mesmo Service não se repete no Appointment e não existe `quantity`.

### Totais
```text
SUM(AppointmentItems.PriceSnapshot)
 -> Appointment.TotalPriceSnapshot
 -> Payment.Amount

SUM(AppointmentItems.DurationMinutesSnapshot)
 -> Appointment.TotalDurationMinutesSnapshot
 -> EndsAt = StartsAt + duração total
```

Combo usa preço/duração próprios.

### Fluxo de seleção
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

Um Appointment possui um único Professional no MVP.

### Status v1
`PENDING`, `CONFIRMED`, `CONFIRMED_BY_CLIENT`, `RESCHEDULE_REQUESTED`, `CANCELLED_BY_CLIENT`, `CANCELLED_BY_BUSINESS`, `EXPIRED`, `COMPLETED`, `NO_SHOW`.

`RESCHEDULED` é evento/histórico, não estado permanente.

### Invariantes
1. `StartsAt < EndsAt`.
2. Pelo menos um AppointmentItem.
3. Duração total corresponde ao intervalo.
4. Preço total corresponde à soma dos itens.
5. Professional habilitado para todos os Services.
6. Intervalo respeita agenda, bloqueios e appointments ativos.
7. Não há sobreposição ativa para o mesmo profissional.
8. CreateAppointment recalcula preço/duração e disponibilidade.
9. Appointment pago só vira CONFIRMED após confirmação financeira confiável.
10. Mudança financeira após pagamento não usa pagamento complementar no MVP; requer fluxo controlado de cancelamento/novo booking.

### AppointmentHistory
Registra mudanças de status, horário, profissional e motivo, preservando auditoria e reagendamentos.

---

## Payment e Refund
Payment representa pagamento integral do Appointment via PIX, CREDIT_CARD ou DEBIT_CARD. `Payment.Amount == Appointment.TotalPriceSnapshot`; sem valor arbitrário, sinal ou parcial; checkout hospedado/tokenizado; confirmação apenas por webhook/API confiável.

Refund é integral e assíncrono. `Refund.Amount == Payment.Amount` no MVP. Nunca comunicar REFUNDED antes da confirmação do gateway.

## Conversation
Estado conversacional não substitui estado do Appointment. IA interpreta intenção e solicita ferramentas estruturadas, mas não altera banco diretamente.

## Subscription e unidade faturável
Billing SaaS é separado de Customer Payments. Mensal: cartão recorrente ou Pix. Anual/12 meses: cartão recorrente. Sem boleto.

Uma Subscription não deve significar automaticamente “todas as unidades do Tenant”. O modelo inclui `SubscriptionUnit`, associando explicitamente a assinatura às Locations faturáveis.

Exemplo futuro:
```text
Tenant: Barbearias Alpha
Business: Alpha Barbearias
Locations: Moema, Tatuapé, Campinas
Subscription
 -> SubscriptionUnit: Moema
 -> SubscriptionUnit: Tatuapé
 -> SubscriptionUnit: Campinas
```

Isso evita cobrar apenas uma assinatura quando múltiplas unidades consomem plataforma, WhatsApp, IA e infraestrutura. Política de preço multi-unidade/franquias fica fora do MVP e poderá ser definida comercialmente depois.

## UsageRecord
Mede AI requests/tokens/custo, WhatsApp inbound/outbound/custo, appointments, payments e refunds por tenant. Evolução multi-location poderá também permitir análise/custo por Location.

---

## Disponibilidade e agenda
```text
Agenda-base recorrente (AvailabilityRule)
- exceções (ScheduleBlock)
- Appointments ativos
= janelas livres
```

Slots não são persistidos. Para múltiplos serviços: Services selecionados -> interseção de ProfessionalServices -> duração total -> janela contínua suficiente. Granularidade de 15 minutos define possíveis inícios, não duração do serviço.

Imprevistos: administrador informa indisponibilidade -> análise de impacto -> confirmação -> ScheduleBlock -> alternativas -> mensagem -> cliente escolhe -> backend revalida -> reagendamento.

---

## Concorrência
Consultar disponibilidade não garante vaga. CreateAppointment revalida e a constraint PostgreSQL é a última barreira. Em corrida, apenas uma reserva persiste; a perdedora recebe `SLOT_UNAVAILABLE` e alternativas atualizadas.

Mensagem padrão:
> **Desculpe, este horário acabou de ser preenchido. Escolha um dos horários disponíveis abaixo.**

---

## Relacionamentos conceituais
```text
Tenant -> Business
Business -> BusinessType
Business -> LegalEntities
Business -> Locations
Location -> LegalEntity (opcional)
Subscription <-> Location via SubscriptionUnit
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

## Regra central
> **IA interpreta -> Backend valida -> Domínio executa.**

Para operações financeiras:
> **IA interpreta -> Usuário autoriza quando necessário -> Backend valida -> Gateway processa -> Webhook confirma.**
