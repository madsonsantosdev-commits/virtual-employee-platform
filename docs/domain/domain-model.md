# Modelo de Domínio v1

> Status: Draft v1 — decisões funcionais aprovadas
> Data: 2026-09-14

## Objetivo
Definir o modelo de domínio inicial da Virtual Employee Platform antes da implementação física da solução .NET e PostgreSQL.

## Princípios
1. Multi-Tenant desde o primeiro dia.
2. Tenant é fronteira de propriedade, segurança e cobrança; não é sinônimo de unidade física.
3. Business representa o negócio/marca; Location representa a unidade operacional.
4. Service e Professional pertencem ao Business; a operação em uma unidade é definida por relacionamentos com Location.
5. Appointment sempre acontece em uma Location.
6. Scheduling é o domínio operacional central.
7. IA interpreta; backend valida; domínio executa.
8. Pagamento só é confirmado por webhook/API confiável do gateway.
9. Preço e duração históricos são preservados por snapshots.
10. Appointments não são apagados fisicamente.
11. Cancelamento operacional e refund financeiro são processos separados.
12. Sem sinal, pagamento parcial, pagamento complementar ou refund parcial no MVP.
13. Double-booking é impedido por validação transacional + constraint PostgreSQL.
14. Um Appointment pode conter um ou mais serviços, mas possui um único Professional no MVP.
15. Privacy by Design: coletar, persistir, expor e compartilhar somente os dados necessários à finalidade.
16. Usuários internos possuem identidade individual; autorização é tenant/business-scoped e baseada em permissões, sem compartilhamento da conta OWNER.
17. Caixa operacional e financeiro gerencial são capacidades distintas: recepção pode registrar pagamento presencial sem obter acesso a relatórios financeiros.

---

## Estrutura organizacional

### Tenant
Fronteira lógica de propriedade, isolamento de dados, segurança e cobrança do assinante. Um Tenant não representa obrigatoriamente um estabelecimento físico.

### Business
Representa o negócio/marca operado pelo Tenant. Mantém políticas comerciais como cancelamento/refund e granularidade de início de agenda (`SlotIntervalMinutes`, default 15).

No MVP a experiência inicial será simples: 1 Tenant -> 1 Business -> 1 Location. O modelo suporta 1 Business -> N Locations.

### BusinessUser e acesso operacional
Um Business pode possuir múltiplos usuários internos, sempre vinculados a identidades individuais. O vínculo conceitual `BusinessUser` associa identidade, Tenant/Business, papel e estado de acesso.

Papéis predefinidos do MVP:
- `OWNER`: titular da conta, acesso integral e autoridade sobre dados sensíveis da conta, segurança, usuários e titularidade;
- `MANAGER`: gestão operacional e acesso financeiro, sem permissão para alterar e-mail/telefone do titular, recuperação ou titularidade;
- `RECEPTIONIST`: agenda, Clients e caixa operacional, sem acesso ao financeiro gerencial ou configurações sensíveis.

A autorização deve ser expressa por capabilities/permissões (ex.: `appointments.manage`, `clients.manage`, `cash.manage`, `financial.read`) para evitar regras de negócio espalhadas por comparação direta de role. No MVP, os conjuntos de permissões podem permanecer predefinidos, sem editor customizável.

Toda ação sensível ou financeira executada por usuário interno deve registrar ator e contexto na trilha de auditoria.

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

## Catálogo, profissionais e unidades

### Service
Service pertence ao Business e representa o catálogo comercial da marca.

Campos principais: `ServiceId`, `TenantId`, `BusinessId`, nome, tipo `SINGLE|COMBO`, preço padrão, duração padrão e `IsActive`.

Preço/duração são definidos pelo backend; combo possui valores próprios; alterações não modificam histórico; sem combo aninhado no MVP.

### LocationService
Define se um Service do Business está disponível em uma determinada Location.

No MVP, `LocationService` representa somente a associação entre Location e Service:
- a existência do vínculo significa que o Service está disponível na Location;
- a remoção do vínculo significa que o Service não está disponível na Location;
- não possui `IsActive`;
- não possui override de preço ou duração;
- Location e Service devem pertencer ao mesmo Business;
- o vínculo é tenant-scoped.

Preço e duração continuam sendo definidos pelo `Service`. Overrides por Location ficam como possibilidade de evolução futura, não fazendo parte do modelo/API v1 atual.

### ServiceComponent
Composição comercial de COMBO. Componentes SINGLE; não calculam automaticamente preço/duração; sem nesting.

### Professional
Professional pertence ao Business, não a uma Location específica. Isso evita duplicar o mesmo profissional quando ele trabalha em mais de uma unidade.

Dados pessoais de Professional devem ser limitados aos necessários para operação e administração.

### ProfessionalLocation
Define em quais Locations um Professional pode operar.

Exemplo:
```text
Arthur -> Moema
Arthur -> Tatuapé
```

O profissional continua sendo uma única entidade do Business.

### ProfessionalService
Matriz Professional × Service. Profissional elegível deve estar habilitado para todos os serviços selecionados. Sem entidade Skill separada no MVP.

A elegibilidade operacional para uma reserva exige simultaneamente:
1. Service disponível na Location via LocationService;
2. Professional associado à Location via ProfessionalLocation;
3. Professional habilitado para todos os Services via ProfessionalService.

### AvailabilityRule
Agenda-base recorrente do profissional **em uma Location**. Portanto inclui `LocationId`.

Isso permite, por exemplo:
```text
Arthur
- segunda: Moema
- terça: Tatuapé
```
Implementação de AvailabilityRule validada em 09/10/2026:

- Campos: Id, TenantId, LocationId, ProfessionalId, DayOfWeek,
  StartTime, EndTime, IsActive, CreatedAt e UpdatedAt.
- Dia da semana de 0 (domingo) a 6 (sábado).
- Horários locais da Location, com início anterior ao fim no mesmo dia.
- Múltiplas janelas por dia são permitidas, sem sobreposição.
- Substituição preserva os registros; janelas omitidas são desativadas.
- Reativar a mesma janela preserva Id e CreatedAt.
- A gestão exige Location, Professional e vínculo ativos e coerência de Business.

### ScheduleBlock
Exceção/indisponibilidade vinculada a uma Location e opcionalmente a um Professional.

- `ProfessionalId != null`: bloqueia aquele profissional naquela Location;
- `ProfessionalId = null`: bloqueia a Location inteira.

Sistema apresenta impacto e oferece alternativas; nenhum cliente é movido sem consentimento.
Implementação validada em 14/10/2026:

- Campos: Id, TenantId, LocationId, ProfessionalId opcional,
  StartsAt, EndsAt, IsActive, CreatedAt e UpdatedAt.
- Instantes normalizados para UTC; início deve preceder o fim.
- Intervalos usam [StartsAt, EndsAt); bloqueios podem atravessar dias.
- Criação idempotente por tenant, operação e chave, com resposta original persistida.
- Desativação preserva o registro e libera os slots afetados.
- Repetir a desativação não altera novamente UpdatedAt.
- A busca desconta bloqueios ativos da unidade e do profissional.
- Avaliação de impacto sobre Appointments permanece pendente.

### Customer

Cliente final pertencente a um Business e ao tenant atual.
Telefone/WhatsApp é identificador operacional principal, mas não PK.

Implementado em 15/10/2026 com nome opcional, telefone obrigatório,
e-mail opcional e datas de criação e atualização em UTC.

A Application normaliza o telefone para `+` seguido de dígitos ASCII,
sem inferir país. A validação é estrutural e não confirma existência
do número nem disponibilidade no WhatsApp.
Nome e e-mail em branco são armazenados como null.

A criação exige Business ativo do tenant atual.
Consultas e escritas respeitam o isolamento entre tenants.
Telefone não possui restrição de unicidade nesta entrega.

A API expõe criação, listagem e consulta por ID.
Evitar enriquecer Customer com dados pessoais sem finalidade definida.

## Appointment — Aggregate Root
Principal agregado operacional do Scheduling.

Campos principais: `AppointmentId`, `TenantId`, `BusinessId`, `LocationId`, `CustomerId`, `ProfessionalId`, `StartsAt`, `EndsAt`, `TotalPriceSnapshot`, `TotalDurationMinutesSnapshot`, `Status`, `ReservationExpiresAt`, cancelamento e auditoria.
### Implementação incremental — 16/10/2026

Implementados Appointment, AppointmentItem, criação idempotente,
consulta por ID, ocupação na busca de slots e expiração configurável.

O agregado nasce PENDING, calcula totais e término a partir dos itens
e preserva snapshots comerciais. A reserva tem prazo configurável,
limitado ao início do atendimento.

A persistência protege referências entre tenants e impede sobreposição
do mesmo profissional, inclusive entre unidades diferentes.
Appointment, itens e resposta idempotente são gravados atomicamente.

O replay preserva a resposta original; a consulta por ID retorna
o estado atual. Expiração não remove itens nem modifica os snapshots.

Desde 19/10/2026, a API também oferece listagem paginada, com filtros
por unidade, profissional, cliente, status e sobreposição de período.
Resultados e contagem respeitam o tenant atual.
A listagem retorna o estado atual e os snapshots comerciais,
sem alterar o agregado.

Desde 19/10/2026, o cancelamento registra CancellationReason e
CancelledAt, preservando itens e snapshots comerciais.
O motivo é obrigatório, recebe Trim e possui limite de 500 caracteres.
As origens operacionais são cliente e estabelecimento.

A mudança de estado e a resposta idempotente são persistidas
atomicamente. O intervalo é liberado após o commit.
O replay preserva a resposta e a auditoria originais.

PENDING vencido segue o fluxo de expiração, sem registrar cancelamento.
Concorrência com outras atualizações é protegida pelo token de Status.

### Reagendamento — implementação em 20/10/2026

Permite alterar Location, Professional e início dentro do mesmo Business,
após revalidar recursos, elegibilidade e disponibilidade.
Preserva identidade, Customer, itens, snapshots e estado operacional.
EndsAt é calculado com a duração persistida, sem recotação pelo catálogo.

PENDING exige prazo vigente. O reagendamento nunca estende a expiração
e a reduz quando o novo início antecede o prazo atual.
O atendimento original ainda não pode ter começado.

Alteração e resposta idempotente são persistidas atomicamente.
Replay preserva a resposta original sem reaplicar a mudança.
O intervalo anterior é liberado após o commit.

Além de TenantId e Status, LocationId, ProfessionalId, StartsAt,
EndsAt e ReservationExpiresAt são tokens de concorrência no EF Core.
A exclusion constraint continua protegendo a ocupação do profissional
em todas as unidades do tenant.

### Histórico de Appointment — implementação em 21/10/2026

AppointmentHistoryEntry pertence ao tenant e referencia um Appointment.
Registra criação, cancelamento, reagendamento e expiração, com data UTC,
estados anterior e novo e motivo quando houver cancelamento.

AppointmentHistoryState preserva Status, LocationId, ProfessionalId,
StartsAt, EndsAt e ReservationExpiresAt.
Os snapshots são imutáveis e não duplicam itens comerciais nem PII.

O histórico é persistido atomicamente com a alteração.
Replay e operações sem mudança não acrescentam registros.
Não há reconstrução retroativa do histórico de agendamentos existentes.

SaveChanges rejeita alteração ou exclusão rastreada de registros históricos.
SQL direto e operações em lote não são abrangidos por essa validação.
A consulta é paginada, isolada por tenant e não modifica o agregado.

Publicação de eventos, reagendamento assistido e integração com
pagamentos e refunds permanecem para próximas etapas.

### Regra de unidade
Todo Appointment ocorre em exatamente uma Location.

Antes da criação/reagendamento, o backend valida:
- Location ativa e pertencente ao Business;
- todos os Services disponíveis nessa Location;
- Professional vinculado à Location;
- Professional habilitado para todos os Services;
- AvailabilityRules daquela Location;
- ScheduleBlocks daquela Location;
- conflitos de Appointment.

### AppointmentItem
Appointment possui 1..N itens, preservando ServiceId, nome, preço e duração em snapshot. Sem quantity no MVP.

Snapshots devem preservar apenas dados comerciais necessários ao histórico; não duplicar PII do Customer/Professional.

### Totais
```text
SUM(AppointmentItems.PriceSnapshot) -> Appointment.TotalPriceSnapshot -> Payment.Amount
SUM(AppointmentItems.DurationMinutesSnapshot) -> Appointment.TotalDurationMinutesSnapshot -> EndsAt
```

Combo usa preço/duração próprios.

### Fluxo
```text
Selecionar Location
-> selecionar 1..N Services disponíveis na Location
-> backend calcula preço/duração
-> opcionalmente sugere combo
-> filtra Professionals da Location
-> mantém apenas os habilitados para TODOS os Services
-> consulta agenda daquele Professional naquela Location
-> busca janela contínua
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
5. Location pertence ao Business e está ativa.
6. Todos os Services estão disponíveis na Location.
7. Professional pertence operacionalmente à Location.
8. Professional está habilitado para todos os Services.
9. Intervalo respeita AvailabilityRules, ScheduleBlocks e appointments ativos da Location.
10. Sem sobreposição ativa para mesmo profissional.
11. CreateAppointment recalcula preço/duração/disponibilidade.
12. Pago só vira CONFIRMED após confirmação financeira confiável.
13. Mudança financeira após pagamento requer cancelamento/novo booking no MVP.

### AppointmentHistory
Preserva auditoria operacional. Registrar mudança necessária sem copiar PII desnecessária; preferir IDs, ator, timestamps, estados, Location e motivo operacional.

---

## Payment e Refund

Payment continua integral:
`Payment.Amount == Appointment.TotalPriceSnapshot`.

### Implementação incremental — 27/10/2026

Payment e PaymentAttempt possuem domínio, persistência e proteção
por tenant. Um Payment de serviço pertence a um Appointment e pode
possuir várias tentativas.

O Payment copia o valor do snapshot comercial do Appointment.
A tentativa copia o valor integral do Payment, sem aceitar valor
arbitrário informado pelo cliente.

A criação exige Appointment PENDING e reserva vigente.
Payment já confirmado ou estornado impede nova tentativa.

A tentativa inicia CREATED. O registro da referência externa pode
levá-la a PENDING; existem operações de processamento, falha,
expiração, cancelamento e confirmação no domínio.

Uma confirmação financeira posterior pode ser registrada mesmo após
falha, expiração ou cancelamento da tentativa, preservando a verdade
financeira. Isso não confirma nem reativa automaticamente o Appointment.

O Payment preserva a referência da tentativa que originou sua liquidação.
Outra tentativa confirmada não substitui essa referência.
A compensação de cobranças excedentes permanece pendente.

A orquestração persiste a intenção antes da chamada externa.
A chamada ao gateway ocorre fora da transação do banco.
A conclusão persiste a alteração da tentativa e o resultado final
na mesma transação.

Resultado externo incerto preserva a tentativa CREATED e sua intenção.
A recuperação usa a mesma chave externa e os parâmetros originais.
Uma nova chave não substitui uma preparação ainda inconclusiva.

Rejeição definitiva marca FAILED e registra a resposta final.
Replay preserva o resultado original sem repetir a chamada externa.

O endpoint de criação foi validado com gateway simulado apenas nos testes.
A rota fica desabilitada por padrão e exige configuração explícita
e uma implementação de IPaymentGateway registrada.

Adaptador real, webhooks autenticados, reconciliação automática,
confirmação operacional do Appointment e refunds permanecem pendentes.

### Canais operacionais previstos

Dois canais operacionais são previstos:

- ONLINE: PIX, CREDIT_CARD ou DEBIT_CARD via checkout hospedado/tokenizado.
  Dados brutos de cartão não trafegam pela aplicação.
  Confirmação financeira vem de webhook/API confiável do provider.
- IN_PERSON: pagamento recebido no estabelecimento e registrado por
  usuário autenticado com permissão de caixa.
  Métodos previstos: PIX, CREDIT_CARD, DEBIT_CARD e CASH.
  A plataforma pode apenas registrar o pagamento realizado externamente,
  por exemplo em maquininha física, sem processar a transação.

Pagamento presencial exige auditoria do ator que o registrou.
IA/Conversation não pode confirmar pagamento presencial.
O fluxo IN_PERSON não está implementado nesta entrega.

Refund permanece integral e assíncrono.
Nunca comunicar REFUNDED antes da confirmação do gateway.

## Conversation
Estado conversacional não substitui Appointment. IA interpreta e solicita ferramentas estruturadas, sem acesso direto ao banco.

Fluxo de IA:
```text
Canal -> Conversation Engine -> Context Builder/Data Minimization -> AI Gateway -> LLM
```

## Subscription e unidade faturável
Billing SaaS separado de Customer Payments. Mensal: cartão recorrente ou Pix. Anual: cartão recorrente. Sem boleto.

Subscription não significa automaticamente todas as unidades. `SubscriptionUnit` associa assinatura às Locations faturáveis.

## UsageRecord
Mede consumo/custos. Telemetria deve usar IDs técnicos e evitar conteúdo pessoal/conversacional quando não necessário. `LocationId` pode ser incluído quando o consumo puder ser atribuído a uma unidade específica.

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
Location
  -> ProfessionalLocation
  -> ProfessionalService
  -> AvailabilityRule
  - ScheduleBlock
  - Appointments ativos
  = janelas livres
```

Para múltiplos serviços: Services disponíveis na Location -> interseção de ProfessionalServices -> duração total -> janela contínua. Slots não são persistidos.

Imprevistos: indisponibilidade na Location -> impacto -> confirmação -> ScheduleBlock -> alternativas -> mensagem -> cliente escolhe -> revalidação -> reagendamento.

## Concorrência
Consultar disponibilidade não garante vaga. CreateAppointment revalida e constraint PostgreSQL é última barreira. Perdedor recebe SLOT_UNAVAILABLE e alternativas.

## Relacionamentos conceituais
```text
Tenant -> Business
Business -> BusinessType
Business -> BusinessUsers -> Identity/Role/Permissions
Business -> LegalEntities
Business -> Locations
Location -> LegalEntity (opcional)
Subscription <-> Location via SubscriptionUnit
Business -> Services
Location <-> Service via LocationService
Service(COMBO) -> ServiceComponents -> Services(SINGLE)
Business -> Professionals
Professional <-> Location via ProfessionalLocation
Professional <-> Service via ProfessionalService
Professional + Location -> AvailabilityRules
Location -> ScheduleBlocks
Business -> Customers
Customer -> Appointments
Location -> Appointments
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
