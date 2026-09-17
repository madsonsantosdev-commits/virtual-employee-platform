# Wireframes PWA v1

> Status: APROVADO — baseline visual inicial do MVP
> Data: 2026-09-17

## 1. Objetivo

Definir a experiência mobile-first da PWA usada pelo proprietário/gestor do estabelecimento. O cliente final continua operando prioritariamente pelo WhatsApp, sem necessidade de instalar aplicativo ou criar uma experiência administrativa própria.

A identidade visual, nome e logo do produto ainda não estão definidos. Os wireframes devem usar identidade neutra até decisão futura de branding.

## 2. Direção visual aprovada

Layout clean, mobile-first, com hierarquia visual simples, cards de leitura rápida, navegação inferior e foco nas informações que exigem atenção do proprietário.

Primeiras telas aprovadas como direção:

1. Onboarding
2. Início / Dashboard
3. Agenda
4. Financeiro

Próximas telas a detalhar: Serviços, Clientes e Configurações.

## 3. Onboarding

Mensagem orientada a resultado: configurar o negócio e o funcionário virtual com poucos passos.

Fluxo conceitual:

`Dados do negócio -> Serviços e preços -> Profissionais e horários -> WhatsApp -> Meios de pagamento -> Teste -> Ativação`

A experiência deve evitar aparência de ERP e apresentar a configuração como treinamento/configuração do funcionário virtual.

## 4. Início / Dashboard

O Dashboard deve priorizar leitura rápida do dia:

- faturamento realizado;
- quantidade de agendamentos;
- receita prevista;
- ticket médio;
- próximos agendamentos;
- acompanhamentos automáticos relevantes;
- acesso conversacional ao funcionário virtual.

O proprietário deve conseguir perguntar, por exemplo: `Como está meu dia?`.

## 5. Agenda

A Agenda apresenta somente dados essenciais do cliente e do atendimento. Fotos de clientes ficam fora do MVP.

Dados principais por horário:

- horário;
- nome do cliente;
- serviço/serviços;
- valor;
- status.

Quando necessário, identificação visual do cliente pode usar iniciais geradas pela interface, sem exigir imagem persistida.

Fotos podem ser consideradas futuramente para Professional, pois fazem sentido como informação cadastrada/controlada pelo estabelecimento. Isso não implica PhotoUrl de Customer no modelo MVP.

A Agenda permite filtros por data e profissional e deve distinguir visualmente estados como confirmado, aguardando pagamento e horário disponível.

## 6. Pagamento pendente — regra de UX aprovada

Todos os agendamentos comerciais exigem pagamento integral para confirmação. Portanto, a interface nunca deve comunicar que existe Appointment confirmado aguardando pagamento.

Fluxo:

`Appointment PENDING -> reserva temporária -> pagamento integral -> webhook do provider -> Payment CONFIRMED -> Appointment CONFIRMED`

Enquanto o pagamento não foi confirmado, o horário permanece temporariamente reservado até `reservationExpiresAt`.

Exemplo aprovado para o Dashboard:

```text
1 pagamento pendente
João • Corte + Barba • 14:00
Reserva expira em 6 min.
```

O texto `cliente não confirmou o pagamento` não deve ser usado. O cliente realiza o pagamento; a confirmação financeira é responsabilidade do provider/webhook.

Se o prazo terminar sem confirmação:

`PENDING -> reservationExpiresAt -> EXPIRED -> horário liberado automaticamente`

## 7. Acompanhamento versus ação necessária

Pagamento pendente normal é acompanhamento e não deve exigir intervenção do proprietário. O funcionário virtual/sistema acompanha a reserva e libera o horário automaticamente quando ela expira.

A área `Precisa de ação` fica reservada a exceções que realmente necessitam decisão ou intervenção, por exemplo:

- indisponibilidade de profissional afetando clientes;
- refund que falhou após tentativas automáticas;
- integração crítica indisponível;
- outra exceção operacional que o backend não possa resolver automaticamente dentro das regras aprovadas.

Princípio de UX:

> O proprietário deve enxergar o que está acontecendo, mas só deve ser interrompido quando realmente precisar decidir alguma coisa.

## 8. Financeiro

A tela financeira prioriza visão objetiva:

- recebido no período;
- previsto;
- estornos;
- agendamentos pagos;
- ticket médio;
- distribuição por forma de pagamento;
- período/filtros;
- relatório/exportação quando aplicável.

Métodos de pagamento do serviço no MVP: PIX, cartão de crédito e cartão de débito. Não existe sinal, adiantamento, pagamento parcial ou boleto.

## 9. Fotos e privacidade

Customer não depende de foto para Agenda ou Dashboard. O MVP não deve criar upload, Blob Storage ou tratamento de imagem de cliente apenas para composição visual da interface.

A integração oficial do WhatsApp não deve ser tratada como fonte arquitetural de foto do cliente. Identidade operacional do Customer permanece baseada nos dados necessários ao atendimento, especialmente nome, telefone/WhatsApp e histórico.

## 10. Princípios consolidados

- mobile-first;
- cliente final no WhatsApp;
- proprietário na PWA + comandos administrativos via WhatsApp;
- dados essenciais antes de elementos decorativos;
- nenhuma dependência de foto de Customer;
- estados financeiros coerentes com Payment/PaymentAttempt;
- pagamento pendente é acompanhamento automático;
- alertas críticos somente quando ação humana for realmente necessária;
- identidade visual/nome/logo serão definidos posteriormente;
- simplicidade e automação antes de amplitude de ERP.

## 11. Próximo detalhamento

Detalhar fluxos e estados das telas Serviços, Clientes e Configurações e, em seguida, converter a baseline aprovada em Features, User Stories e Tasks do backlog MVP.
