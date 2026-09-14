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
13. Privacy by Design: coletar, persistir, expor e compartilhar somente os dados necessários à finalidade.

---

## Estrutura organizacional

### Tenant
Fronteira lógica de propriedade, isolamento de dados, segurança e cobrança do assinante. Um Tenant não representa obrigatoriamente um estabelecimento físico.

### Business
Representa o negócio/marca operado pelo Tenant. Mantém políticas comerciais como cancelamento/refund e granularidade de início de agenda (`SlotIntervalMinutes`, default 15).

No MVP a experiência inicial será simples: 1 Tenant -> 1 Business -> 1 Location. O modelo suporta 1 Business -> N Locations.

### BusinessType
Classificação do negócio. Tipos oficiais possuem escopo global; tipos customizados pertencem ao Tenant. Unicidade customizada é tenant-scoped.

### LegalEntity
Representa identidade jurídica/fiscal associada ao Business.

Campos conceituais: `TenantId`, `BusinessId`, `EntityType (PERSON|COMPANY)`, `DocumentType (CPF|CNPJ)`, `DocumentNumber`, `CountryCode`, `LegalName`, `TradeName`, `IsPrimary`, `IsActive`.

Regras de privacidade e domínio:
- CPF/CNPJ normalizados e validados no backend;
- documento fiscal nunca é PK;
- documento completo não aparece em logs;
- UI mascara documento quando visualização integral não for necessária;
- alteração de dados fiscais é auditável;
- acesso requer autorização;
- IA não recebe CPF/CNPJ sem necessidade operacional explícita;
- retenção/exclusão segue política definida por finalidade e obrigação aplicável.

### Location
Unidade operacional física ou virtual. Mantém nome, telefone, endereço, país e timezone. Pode referenciar LegalEntity.

> **Tenant = fronteira de propriedade, segurança e cobrança. Business = negócio/marca. Location = unidade operacional física ou virtual.**

Franquias não serão módulo do MVP. Franqueados independentes podem futuramente possuir Tenants separados.

---

## Catálogo e profissionais

### Service
Catálogo comercial do estabelecimento: `ServiceId`, `TenantId`, `BusinessId`, nome, descrição, tipo `SINGLE|COMBO`, preço, duração, RequiresPayment e IsActive.

Preço/duração definidos pelo backend; combo possui valores próprios; alterações não modificam histórico; sem combo aninhado no MVP. Escopo Business x Location será fechado na próxima revisão.

### ServiceComponent
Composição comercial de COMBO. Componentes SINGLE; não calculam automaticamente preço/duração; sem nesting.

### Professional
Profissional que executa serviços. O relacionamento definitivo com Location será fechado na revisão multi-unidade.

Dados pessoais de Professional devem ser limitados aos necessários para operação e administração. Informações não necessárias ao Scheduling não pertencem automaticamente ao agregado.

### ProfessionalService
Matriz Professional × Service. Profissional elegível deve estar habilitado para todos os serviços selecionados. Sem entidade Skill separada no MVP.

### AvailabilityRule
Agenda-base recorrente do profissional. Aplicação permite reutilizar/copiar configuração e alterar exceções.

### ScheduleBlock
Exceção/indisponibilidade. Sistema apresenta impacto e oferece alternativas; nenhum cliente é movido sem consentimento.

### Customer
Cliente final. Telefone/WhatsApp é identificador operacional principal, mas não PK.

Dados previstos no MVP: nome opcional, telefone/WhatsApp obrigatório operacionalmente e e-mail opcional. Evitar enriquecer Customer com dados pessoais sem finalidade definida.

Atendimento de direitos LGPD deve conseguir localizar os registros relacionados ao titular sem tornar telefone/CPF chave primária.

---

## Appointment — Aggregate Root
Principal agregado operacional do Scheduling.

Campos principais: `AppointmentId`, `TenantId`, `BusinessId`, `CustomerId`, `ProfessionalId`, `StartsAt`, `EndsAt`, `TotalPriceSnapshot`, `TotalDurationMinutesSnapshot`, `Status`, `ReservationExpiresAt`, cancelamento e auditoria.

`LocationId` será incorporado quando fecharmos Scheduling multi-location.

### AppointmentItem
Appointment possui 1..N itens, preservando ServiceId, nome, preço e duração em snapshot. Sem quantity no MVP.

Snapshots devem preservar apenas dados comerciais necessários ao histórico; não duplicar PII do Customer/Professional nos itens.

### Totais
```text
SUM(AppointmentItems.PriceSnapshot) -> Appointment.TotalPriceSnapshot -> Payment.Amount
SUM(AppointmentItems.DurationMinutesSnapshot) -> Appointment.TotalDurationMinutesSnapshot -> EndsAt
```

Combo usa preço/duração próprios.

### Fluxo
```text
Selecionar 1..N serviços
-> backend calcula preço/duração
-> opcionalmente sugere combo
-> profissionais habilitados para TODOS
-> janela contínua
-> cliente escolhe
-> CreateAppointment revalida
-> PENDING
-> pagamento integral
-> webhook confiável
-> CONFIRMED
```

### Status
`PENDING`, `CONFIRMED`, `CONFIRMED_BY_CLIENT`, `RESCHEDULE_REQUESTED`, `CANCELLED_BY_CLIENT`, `CANCELLED_BY_BUSINESS`, `EXPIRED`, `COMPLETED`, `NO_SHOW`.

### Invariantes
1. StartsAt < EndsAt.
2. Pelo menos um AppointmentItem.
3. Duração total corresponde ao intervalo.
4. Preço total corresponde à soma dos itens.
5. Professional habilitado para todos os Services.
6. Intervalo respeita agenda, bloqueios e appointments ativos.
7. Sem sobreposição ativa para mesmo profissional.
8. CreateAppointment recalcula preço/duração/disponibilidade.
9. Pago só vira CONFIRMED após confirmação financeira confiável.
10. Mudança financeira após pagamento requer cancelamento/novo booking no MVP.

### AppointmentHistory
Preserva auditoria operacional. Registrar mudança necessária sem copiar PII desnecessária; preferir IDs, ator, timestamps, estados e motivo operacional.

---

## Payment e Refund
Payment integral via PIX, CREDIT_CARD ou DEBIT_CARD. `Payment.Amount == Appointment.TotalPriceSnapshot`. Checkout hospedado/tokenizado; dados brutos de cartão não trafegam pela aplicação; confirmação apenas por webhook/API confiável.

Refund integral e assíncrono. Nunca comunicar REFUNDED antes da confirmação do gateway.

## Conversation
Estado conversacional não substitui Appointment. IA interpreta e solicita ferramentas estruturadas, sem acesso direto ao banco.

Conversation e conteúdo de mensagens são dados com retenção própria. Não presumir retenção indefinida.

Fluxo de IA:
```text
Canal -> Conversation Engine -> Context Builder/Data Minimization -> AI Gateway -> LLM
```

O modelo recebe apenas contexto necessário. CPF/CNPJ, cartão, secrets e dados financeiros irrelevantes são excluídos do contexto.

## Subscription e unidade faturável
Billing SaaS separado de Customer Payments. Mensal: cartão recorrente ou Pix. Anual: cartão recorrente. Sem boleto.

Subscription não significa automaticamente todas as unidades. `SubscriptionUnit` associa assinatura às Locations faturáveis.

## UsageRecord
Mede consumo/custos. Telemetria deve usar IDs técnicos e evitar conteúdo pessoal/conversacional quando não necessário.

---

## Privacidade / LGPD — invariantes de domínio

1. Toda nova categoria de dado pessoal exige finalidade documentada antes de entrar no modelo.
2. APIs não retornam PII por conveniência; DTOs expõem somente o necessário ao caso de uso.
3. CPF/CNPJ e identificadores pessoais não são usados como PK.
4. PII não deve ser duplicada em snapshots sem necessidade histórica real.
5. Logs/auditoria preferem IDs e metadados técnicos.
6. Retenção é definida por categoria; soft-delete não é justificativa para retenção eterna.
7. Processos devem permitir localizar/corrigir/exportar/anonimizar/eliminar dados quando juridicamente aplicável.
8. Compartilhamento com provedores externos segue minimização.
9. Papéis Controlador/Operador são avaliados por finalidade/fluxo.
10. Decisão jurídica sobre base legal, retenção obrigatória ou incidente não é delegada ao LLM.

Detalhamento: `docs/architecture/privacy-lgpd-v1.md`.

---

## Disponibilidade e agenda
```text
AvailabilityRule - ScheduleBlock - Appointments ativos = janelas livres
```

Para múltiplos serviços: Services -> interseção ProfessionalServices -> duração total -> janela contínua. Slots não são persistidos.

Imprevistos: indisponibilidade -> impacto -> confirmação -> ScheduleBlock -> alternativas -> mensagem -> cliente escolhe -> revalidação -> reagendamento.

## Concorrência
Consultar disponibilidade não garante vaga. CreateAppointment revalida e constraint PostgreSQL é última barreira. Perdedor recebe SLOT_UNAVAILABLE e alternativas.

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

Para privacidade:
> **Coletar o necessário -> limitar finalidade/acesso -> compartilhar o mínimo -> reter pelo período definido -> atender direitos do titular.**
